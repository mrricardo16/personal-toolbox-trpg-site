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

    public Task<PlayerCombatIntentResult> MeleeAttackAsync(PlayerMeleeAttackIntent intent) =>
        Task.FromResult(PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.InvalidIntent));

    public Task<PlayerCombatIntentResult> RespondAsync(PlayerRespondIntent intent) =>
        Task.FromResult(PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.InvalidIntent));

    public Task<PlayerCombatIntentResult> PassAsync(PlayerPassIntent intent) =>
        Task.FromResult(PlayerCombatIntentResult.Failure(PlayerCombatIntentErrorCode.InvalidIntent));
}
