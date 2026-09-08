namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record CharacterSnapshot(
    Guid CharacterId,
    Guid OwnerPlayerId,
    string Name,
    IReadOnlyDictionary<string, int> CheckValues,
    CharacterHealthSnapshot? Health);

public sealed record CharacterHealthSnapshot(
    int CurrentHp,
    int MaxHp,
    bool MajorWound,
    bool Unconscious,
    bool Dying,
    bool Dead,
    bool Stabilized);

public sealed record GameSnapshot(
    Guid RoomId,
    long Revision,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CharacterSnapshot> Characters,
    GameCheckRecord? LastCheck = null,
    CombatSnapshot? Combat = null);

public static class GameProjection
{
    public static GameSnapshot Build(MultiplayerGameState state, Guid viewerPlayerId)
    {
        return new GameSnapshot(
            state.RoomId,
            state.Revision,
            state.Status.ToString(),
            state.CreatedAt,
            state.Characters
                .Select(character => new CharacterSnapshot(
                    character.CharacterId,
                    character.OwnerPlayerId,
                    character.Name,
                    character.OwnerPlayerId == viewerPlayerId
                        ? new Dictionary<string, int>(character.CheckValues, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    character.OwnerPlayerId == viewerPlayerId
                        ? new CharacterHealthSnapshot(
                            character.Health.CurrentHp,
                            character.Health.MaxHp,
                            character.Health.MajorWound,
                            character.Health.Unconscious,
                            character.Health.Dying,
                            character.Health.Dead,
                            character.Health.Stabilized is not null)
                        : null))
                .ToArray(),
            state.LastCheck,
            BuildCombat(state.Combat, viewerPlayerId));
    }

    private static CombatSnapshot? BuildCombat(CombatSession? session, Guid viewerPlayerId)
    {
        if (session is null)
        {
            return null;
        }

        var viewerParticipantIds = session.Participants
            .Where(participant => participant.OwnerPlayerId == viewerPlayerId)
            .Select(participant => participant.ParticipantId.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (viewerParticipantIds.Count == 0)
        {
            return null;
        }

        var currentActorParticipantId = session.TurnIndex >= 0 && session.TurnIndex < session.Order.Count
            ? session.Order[session.TurnIndex].Value
            : null;
        return new CombatSnapshot(
            session.Active,
            session.Round,
            currentActorParticipantId,
            session.Participants
                .Select(participant => new CombatParticipantSnapshot(
                    participant.ParticipantId.Value,
                    participant.CharacterId,
                    participant.Label,
                    participant.Side,
                    participant.Active,
                    participant.ParticipantId.Value == currentActorParticipantId,
                    participant.OwnerPlayerId == viewerPlayerId,
                    participant.OwnerPlayerId == viewerPlayerId
                        ? new CombatParticipantStatsSnapshot(participant.Dex, participant.Fighting, participant.Dodge)
                        : null))
                .ToArray(),
            session.LastExchange is null
                ? null
                : new CombatExchangeSnapshot(
                    session.LastExchange.Outcome,
                    session.LastExchange.WinnerParticipantId?.Value,
                    session.LastExchange.DamageDisposition is not null
                    && session.DamageDispositions.TryGetValue(session.LastExchange.ExchangeId, out var disposition)
                    && disposition.Status == DamageDispositionStatus.Pending),
            session.PendingExchange is null
                ? null
                : new CombatPendingSnapshot(
                    GetPendingRole(session.PendingExchange, viewerParticipantIds),
                    "awaiting_response"),
            BuildLastDamage(session.DamageDispositions),
            BuildViewerActions(session, viewerPlayerId, currentActorParticipantId));
    }

    private static CombatViewerActionsSnapshot? BuildViewerActions(
        CombatSession session,
        Guid viewerPlayerId,
        string? currentActorParticipantId)
    {
        if (!session.Active)
        {
            return null;
        }

        var hasProgressionBlocker = session.PendingExchange is not null
            || session.DamageDispositions.Values.Any(disposition => disposition.Status == DamageDispositionStatus.Pending);
        var currentActor = session.Participants.SingleOrDefault(
            participant => participant.ParticipantId.Value == currentActorParticipantId);
        var ownsCurrentInvestigator = currentActor is
        {
            Active: true,
            Kind: "investigator",
            CharacterId: Guid,
            OwnerPlayerId: Guid owner
        } && owner == viewerPlayerId;

        if (ownsCurrentInvestigator && !hasProgressionBlocker)
        {
            var eligibleTargets = session.Participants
                .Where(participant => participant.Active && participant.Side != currentActor!.Side)
                .Select(participant => participant.ParticipantId.Value)
                .ToArray();
            return new CombatViewerActionsSnapshot(
                currentActor!.CharacterId,
                eligibleTargets.Length > 0,
                true,
                eligibleTargets,
                null);
        }

        var pending = session.PendingExchange;
        var defender = pending is null
            ? null
            : session.Participants.SingleOrDefault(
                participant => participant.ParticipantId.Value == pending.DefenderParticipantId.Value);
        var ownsExactHumanDefender = pending is not null
            && defender is
            {
                Active: true,
                Kind: "investigator",
                CharacterId: Guid,
                OwnerPlayerId: Guid defenderOwner
            }
            && defenderOwner == viewerPlayerId
            && pending.DefenderOwnerPlayerId == viewerPlayerId;
        if (!ownsExactHumanDefender)
        {
            return null;
        }

        return new CombatViewerActionsSnapshot(
            null,
            false,
            false,
            [],
            new CombatPendingResponseSnapshot(
                pending!.ExchangeId,
                pending.AvailableResponses.Select(ToCombatResponseWireValue).ToArray()));
    }

    private static string ToCombatResponseWireValue(CombatResponse response) => response switch
    {
        CombatResponse.Dodge => "dodge",
        CombatResponse.FightBack => "fight_back",
        _ => throw new CombatDamageStateInvariantException("Unsupported canonical Combat response.")
    };

    private static CombatDamageSnapshot? BuildLastDamage(
        IReadOnlyDictionary<string, DamageDispositionState> damageDispositions)
    {
        var latest = damageDispositions.Values
            .Where(disposition => disposition.Status == DamageDispositionStatus.Consumed && disposition.Result is not null)
            .Select(disposition => disposition.Result!)
            .OrderByDescending(result => result.ResolvedAt)
            .ThenByDescending(result => result.ExchangeId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        var outcome = latest.Outcome switch
        {
            CombatDamageOutcome.Applied => "applied",
            CombatDamageOutcome.TargetAlreadyIneligible => "target_already_ineligible",
            _ => throw new CombatDamageStateInvariantException(
                $"Combat damage exchange '{latest.ExchangeId}' has an unsupported canonical outcome.")
        };
        return new CombatDamageSnapshot(
            latest.ExchangeId,
            latest.OwnerParticipantId.Value,
            latest.TargetParticipantId.Value,
            outcome,
            latest.NetDamage,
            latest.TargetDefeated);
    }

    private static string GetPendingRole(
        PendingCombatExchange pendingExchange,
        IReadOnlySet<string> viewerParticipantIds)
    {
        if (viewerParticipantIds.Contains(pendingExchange.AttackerParticipantId.Value))
        {
            return "attacker";
        }

        return viewerParticipantIds.Contains(pendingExchange.DefenderParticipantId.Value)
            ? "defender"
            : "observer";
    }
}
