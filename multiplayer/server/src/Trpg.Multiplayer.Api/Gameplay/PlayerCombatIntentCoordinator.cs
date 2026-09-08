namespace Trpg.Multiplayer.Api.Gameplay;

internal sealed record PlayerMeleeAttackIntent(
    Guid RoomId,
    Guid PlayerId,
    long ExpectedGameRevision,
    Guid ActorCharacterId,
    string TargetParticipantId);

internal sealed record PlayerRespondIntent(
    Guid RoomId,
    Guid PlayerId,
    long ExpectedGameRevision,
    string ExchangeId,
    CombatResponse Response);

internal sealed record PlayerPassIntent(
    Guid RoomId,
    Guid PlayerId,
    long ExpectedGameRevision,
    Guid ActorCharacterId);

internal enum PlayerCombatIntentErrorCode
{
    InvalidIntent,
    InvalidResponse,
    InvalidSession,
    NotMember,
    RoomNotFound,
    GameNotFound,
    ActorNotOwned,
    DefenderNotOwned,
    StaleGameRevision,
    CombatInactive,
    NotCurrentActor,
    TargetNotEligible,
    ExchangeNotPending,
    ProgressionBlocked,
    CombatConsistencyFailure
}

internal sealed record PlayerCombatIntentError(
    PlayerCombatIntentErrorCode Code,
    long? CurrentGameRevision = null);

internal sealed record PlayerCombatIntentResult(
    GameSnapshot? Snapshot,
    PlayerCombatIntentError? Error)
{
    public bool IsSuccess => Error is null;

    public static PlayerCombatIntentResult Success(GameSnapshot snapshot) => new(snapshot, null);

    public static PlayerCombatIntentResult Failure(
        PlayerCombatIntentErrorCode code,
        long? currentGameRevision = null) => new(null, new PlayerCombatIntentError(code, currentGameRevision));
}

internal interface IPlayerCombatIntentCoordinator
{
    Task<PlayerCombatIntentResult> MeleeAttackAsync(PlayerMeleeAttackIntent intent);

    Task<PlayerCombatIntentResult> RespondAsync(PlayerRespondIntent intent);

    Task<PlayerCombatIntentResult> PassAsync(PlayerPassIntent intent);
}

internal sealed class PlayerCombatIntentCoordinator(
    IGameCoordinator games,
    IInternalCombatResolutionCoordinator combat) : IPlayerCombatIntentCoordinator
{
    private readonly IGameCoordinator games = games;
    private readonly IInternalCombatResolutionCoordinator combat = combat;

    public async Task<PlayerCombatIntentResult> MeleeAttackAsync(PlayerMeleeAttackIntent intent)
    {
        // 修改时间：2026-09-08 13:01:58
        // 修改说明：按查看者投影预校验玩家近战意图，并只沿 Begin/Resolve 返回的权威状态继续 NPC 响应与精确伤害处理。
        // 修改原因：避免应用层读取存储、推断伤害结果或让客户端/房主选择 NPC 响应，同时保留每个内部转换的独立提交修订。
        // 业务影响：新增玩家当前调查员对投影合格目标的近战编排；人类防御者仅提交 Begin，NPC 防御者由私有快照策略继续处理。
        var projection = await games.GetProjectionAsync(intent.RoomId, intent.PlayerId);
        if (!projection.IsSuccess)
        {
            return MapProjectionFailure(projection.Error!.Code);
        }

        var snapshot = projection.Value!;
        if (snapshot.Revision != intent.ExpectedGameRevision)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.StaleGameRevision,
                snapshot.Revision);
        }

        var combatSnapshot = snapshot.Combat;
        if (combatSnapshot is null || !combatSnapshot.Active)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.CombatInactive,
                snapshot.Revision);
        }

        var actor = combatSnapshot.Participants.SingleOrDefault(
            participant => participant.CharacterId == intent.ActorCharacterId);
        if (actor is null || !actor.ViewerOwned)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.ActorNotOwned,
                snapshot.Revision);
        }

        if (!actor.Active
            || !actor.Current
            || combatSnapshot.CurrentActorParticipantId != actor.ParticipantId)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.NotCurrentActor,
                snapshot.Revision);
        }

        var actions = combatSnapshot.ViewerActions;
        if (actions?.ActorCharacterId != intent.ActorCharacterId || !actions.CanMeleeAttack)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.ProgressionBlocked,
                snapshot.Revision);
        }

        if (!actions.EligibleTargetParticipantIds.Contains(intent.TargetParticipantId, StringComparer.Ordinal))
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.TargetNotEligible,
                snapshot.Revision);
        }

        var begin = await combat.BeginOpposedExchangeAsync(new BeginOpposedExchangeCommand(
            intent.RoomId,
            intent.PlayerId,
            intent.ExpectedGameRevision,
            $"character:{intent.ActorCharacterId}",
            intent.TargetParticipantId));
        if (!begin.IsSuccess)
        {
            return MapTransitionFailure(begin.Error!.Code, snapshot.Revision);
        }

        var beginState = begin.Value!.State;
        var beginCombat = beginState.Combat
            ?? throw new InvalidOperationException("Committed Begin result omitted its combat session.");
        var pending = beginCombat.PendingExchange
            ?? throw new InvalidOperationException("Committed Begin result omitted its pending exchange.");
        var defender = beginCombat.Participants.Single(
            participant => participant.ParticipantId == pending.DefenderParticipantId);

        if (defender.OwnerPlayerId is null)
        {
            var policy = defender.NpcResponsePolicy
                ?? throw new InvalidOperationException("Committed NPC defender omitted its response policy.");
            if (!pending.AvailableResponses.Contains(policy))
            {
                return PlayerCombatIntentResult.Failure(
                    PlayerCombatIntentErrorCode.CombatConsistencyFailure,
                    beginState.Revision);
            }

            var resolved = await combat.ResolvePendingExchangeAsync(new ResolvePendingExchangeCommand(
                intent.RoomId,
                null,
                beginState.Revision,
                pending.ExchangeId,
                policy));
            if (!resolved.IsSuccess)
            {
                return MapTransitionFailure(resolved.Error!.Code, beginState.Revision);
            }

            var damageFailure = await ResolveExactPendingDamageAsync(
                intent.RoomId,
                pending.ExchangeId,
                resolved.Value!.State);
            if (damageFailure is not null)
            {
                return damageFailure;
            }
        }

        return await GetFinalProjectionAsync(intent.RoomId, intent.PlayerId);
    }

    public async Task<PlayerCombatIntentResult> RespondAsync(PlayerRespondIntent intent)
    {
        // 修改时间：2026-09-08 13:20:43
        // 修改说明：按当前查看者投影预校验人类防御者响应，并以 Resolve 返回状态决定是否消费该交换的精确待处理伤害。
        // 修改原因：确保只有收到精确 PendingResponse 的防御者能发起响应，同时让权威 Resolve 继续校验所有真实状态约束。
        // 业务影响：实现玩家战斗响应编排；拒绝路径不掷骰、不转换状态，成功路径返回转换后的全新查看者投影。
        var projection = await games.GetProjectionAsync(intent.RoomId, intent.PlayerId);
        if (!projection.IsSuccess)
        {
            return MapProjectionFailure(projection.Error!.Code);
        }

        var snapshot = projection.Value!;
        if (snapshot.Revision != intent.ExpectedGameRevision)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.StaleGameRevision,
                snapshot.Revision);
        }

        var combatSnapshot = snapshot.Combat;
        if (combatSnapshot is null || !combatSnapshot.Active)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.CombatInactive,
                snapshot.Revision);
        }

        if (combatSnapshot.Pending is null)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.ExchangeNotPending,
                snapshot.Revision);
        }

        var pending = combatSnapshot.ViewerActions?.PendingResponse;
        if (pending is null)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.DefenderNotOwned,
                snapshot.Revision);
        }

        if (!string.Equals(pending.ExchangeId, intent.ExchangeId, StringComparison.Ordinal))
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.ExchangeNotPending,
                snapshot.Revision);
        }

        var wireResponse = ToCombatResponseWireValue(intent.Response);
        if (wireResponse is null
            || !pending.AvailableResponses.Contains(wireResponse, StringComparer.Ordinal))
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.InvalidResponse,
                snapshot.Revision);
        }

        var resolved = await combat.ResolvePendingExchangeAsync(new ResolvePendingExchangeCommand(
            intent.RoomId,
            intent.PlayerId,
            intent.ExpectedGameRevision,
            pending.ExchangeId,
            intent.Response));
        if (!resolved.IsSuccess)
        {
            return MapTransitionFailure(resolved.Error!.Code, snapshot.Revision);
        }

        var damageFailure = await ResolveExactPendingDamageAsync(
            intent.RoomId,
            pending.ExchangeId,
            resolved.Value!.State);
        if (damageFailure is not null)
        {
            return damageFailure;
        }

        return await GetFinalProjectionAsync(intent.RoomId, intent.PlayerId);
    }

    public async Task<PlayerCombatIntentResult> PassAsync(PlayerPassIntent intent)
    {
        // 修改时间：2026-09-08 17:31:25
        // 修改说明：按查看者投影预校验玩家当前调查员的 Pass 意图，并只调用权威 Pass 转换后返回全新投影。
        // 修改原因：阻止未拥有、非当前、过期或存在待处理战斗事项的请求绕过权威回合、轮次和濒死调度规则。
        // 业务影响：玩家仅能为其当前调查员提交一次规范 Pass；回合推进、轮次环绕、Dying 和发布仍由 canonical 转换负责。
        var projection = await games.GetProjectionAsync(intent.RoomId, intent.PlayerId);
        if (!projection.IsSuccess)
        {
            return MapProjectionFailure(projection.Error!.Code);
        }

        var snapshot = projection.Value!;
        if (snapshot.Revision != intent.ExpectedGameRevision)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.StaleGameRevision,
                snapshot.Revision);
        }

        var combatSnapshot = snapshot.Combat;
        if (combatSnapshot is null || !combatSnapshot.Active)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.CombatInactive,
                snapshot.Revision);
        }

        var actor = combatSnapshot.Participants.SingleOrDefault(
            participant => participant.CharacterId == intent.ActorCharacterId);
        if (actor is null || !actor.ViewerOwned)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.ActorNotOwned,
                snapshot.Revision);
        }

        if (!actor.Active
            || !actor.Current
            || combatSnapshot.CurrentActorParticipantId != actor.ParticipantId)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.NotCurrentActor,
                snapshot.Revision);
        }

        var actions = combatSnapshot.ViewerActions;
        if (actions?.ActorCharacterId != intent.ActorCharacterId || !actions.CanPass)
        {
            return PlayerCombatIntentResult.Failure(
                PlayerCombatIntentErrorCode.ProgressionBlocked,
                snapshot.Revision);
        }

        var passed = await combat.PassCombatTurnAsync(new PassCombatTurnCommand(
            intent.RoomId,
            intent.PlayerId,
            intent.ExpectedGameRevision));
        if (!passed.IsSuccess)
        {
            return MapTransitionFailure(passed.Error!.Code, snapshot.Revision);
        }

        return await GetFinalProjectionAsync(intent.RoomId, intent.PlayerId);
    }

    private async Task<PlayerCombatIntentResult?> ResolveExactPendingDamageAsync(
        Guid roomId,
        string exchangeId,
        MultiplayerGameState resolvedState)
    {
        var resolvedCombat = resolvedState.Combat
            ?? throw new InvalidOperationException("Committed Resolve result omitted its combat session.");
        if (!resolvedCombat.DamageDispositions.TryGetValue(exchangeId, out var disposition)
            || disposition.Status != DamageDispositionStatus.Pending)
        {
            return null;
        }

        var damage = await combat.ResolveCombatDamageAsync(new ResolveCombatDamageCommand(
            roomId,
            resolvedState.Revision,
            exchangeId));
        return damage.IsSuccess
            ? null
            : MapTransitionFailure(damage.Error!.Code, resolvedState.Revision);
    }

    private async Task<PlayerCombatIntentResult> GetFinalProjectionAsync(Guid roomId, Guid playerId)
    {
        var projection = await games.GetProjectionAsync(roomId, playerId);
        return projection.IsSuccess
            ? PlayerCombatIntentResult.Success(projection.Value!)
            : MapProjectionFailure(projection.Error!.Code);
    }

    private static string? ToCombatResponseWireValue(CombatResponse response) => response switch
    {
        CombatResponse.Dodge => "dodge",
        CombatResponse.FightBack => "fight_back",
        _ => null
    };

    private static PlayerCombatIntentResult MapProjectionFailure(GameErrorCode code) => code switch
    {
        GameErrorCode.RoomNotFound => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.RoomNotFound),
        GameErrorCode.RoomClosed => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.InvalidSession),
        GameErrorCode.NotMember => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.NotMember),
        GameErrorCode.GameNotFound => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.GameNotFound),
        _ => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.InvalidIntent)
    };

    private static PlayerCombatIntentResult MapTransitionFailure(GameErrorCode code, long currentRevision) => code switch
    {
        GameErrorCode.RoomNotFound => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.RoomNotFound),
        GameErrorCode.RoomClosed => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.InvalidSession),
        GameErrorCode.NotMember => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.NotMember),
        GameErrorCode.GameNotFound => PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.GameNotFound),
        GameErrorCode.StateConflict => PlayerCombatIntentResult.Failure(
            PlayerCombatIntentErrorCode.StaleGameRevision,
            currentRevision),
        GameErrorCode.InvalidCombat or GameErrorCode.EndedCombat => PlayerCombatIntentResult.Failure(
            PlayerCombatIntentErrorCode.CombatInactive,
            currentRevision),
        GameErrorCode.InvalidExchange => PlayerCombatIntentResult.Failure(
            PlayerCombatIntentErrorCode.ExchangeNotPending,
            currentRevision),
        GameErrorCode.InvalidResponse => PlayerCombatIntentResult.Failure(
            PlayerCombatIntentErrorCode.InvalidResponse,
            currentRevision),
        GameErrorCode.PendingConflict => PlayerCombatIntentResult.Failure(
            PlayerCombatIntentErrorCode.ProgressionBlocked,
            currentRevision),
        GameErrorCode.InvalidParticipant => PlayerCombatIntentResult.Failure(
            PlayerCombatIntentErrorCode.TargetNotEligible,
            currentRevision),
        _ => PlayerCombatIntentResult.Failure(
            PlayerCombatIntentErrorCode.CombatConsistencyFailure,
            currentRevision)
    };
}
