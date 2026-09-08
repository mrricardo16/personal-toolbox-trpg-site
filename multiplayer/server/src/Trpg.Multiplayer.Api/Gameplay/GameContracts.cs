namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record InitializeGameCommand(
    Guid RoomId,
    Guid HostPlayerId,
    IReadOnlyList<InitializeCharacterCommand> Characters);

public sealed record InitializeCharacterCommand(
    Guid PlayerId,
    string Name,
    IReadOnlyDictionary<string, int> CheckValues,
    CharacterHealthSetup? Health);

public sealed record CharacterHealthSetup(int CurrentHp, int MaxHp, int Con);

internal sealed record StartCombatCommand(
    Guid RoomId,
    Guid AuthorizedPlayerId,
    long ExpectedGameRevision,
    IReadOnlyList<Guid> CharacterIds,
    IReadOnlyList<OpponentDefinition> Opponents);

internal sealed record BeginOpposedExchangeCommand(
    Guid RoomId,
    Guid RequestingPlayerId,
    long ExpectedGameRevision,
    string AttackerParticipantId,
    string DefenderParticipantId);

internal sealed record ResolvePendingExchangeCommand(
    Guid RoomId,
    Guid? RequestingPlayerId,
    long ExpectedGameRevision,
    string ExchangeId,
    CombatResponse Response);

internal sealed record PassCombatTurnCommand(
    Guid RoomId,
    Guid RequestingPlayerId,
    long ExpectedGameRevision);

internal sealed record EndCombatCommand(
    Guid RoomId,
    Guid AuthorizedPlayerId,
    long ExpectedGameRevision,
    string Reason);

internal sealed record ResolveCombatDamageCommand(
    Guid RoomId,
    long ExpectedGameRevision,
    string ExchangeId);

internal sealed record OpponentDefinition(
    string Label,
    int Dex,
    int Fighting,
    int Dodge,
    IReadOnlyList<CombatResponse> AvailableResponses,
    int ResponseAllowance,
    CombatResponse NpcResponsePolicy,
    int Str,
    int Siz,
    int CurrentHp,
    int MaxHp,
    int FixedArmor,
    CombatWeaponProfile Weapon);

internal sealed record StartCombatResult(MultiplayerGameState State);

internal sealed record BeginOpposedExchangeResult(MultiplayerGameState State);

internal sealed record ResolvePendingExchangeResult(MultiplayerGameState State);

internal sealed record PassCombatTurnResult(MultiplayerGameState State);

internal sealed record EndCombatResult(MultiplayerGameState State);

internal sealed record ResolveCombatDamageResult(
    MultiplayerGameState State,
    CombatDamageResult Damage);

internal sealed class CombatDamageStateInvariantException(string message) : Exception(message);

internal sealed class CombatDamageCommitInvariantException(string message) : Exception(message);

public sealed record ResolveCheckCommand(
    Guid RoomId,
    Guid PlayerId,
    Guid CharacterId,
    string CheckKey,
    string Difficulty,
    int BonusDice,
    int PenaltyDice);

public sealed record GameCheckRecord(
    Guid CheckId,
    Guid PlayerId,
    Guid CharacterId,
    string CheckKey,
    int Target,
    int Roll,
    string SuccessLevel,
    bool Passed,
    long GameRevision,
    DateTimeOffset CreatedAt);

public sealed record GameCheckResult(
    GameSnapshot Snapshot,
    CheckResolutionResult Check);

public sealed record CombatParticipantStatsSnapshot(
    int Dex,
    int Fighting,
    int Dodge);

public sealed record CombatParticipantSnapshot(
    string ParticipantId,
    Guid? CharacterId,
    string Label,
    string Side,
    bool Active,
    bool Current,
    bool ViewerOwned,
    CombatParticipantStatsSnapshot? Stats);

public sealed record CombatExchangeSnapshot(
    string Outcome,
    string? WinnerParticipantId,
    bool DispositionPending);

public sealed record CombatPendingSnapshot(
    string Role,
    string Status);

public sealed record CombatDamageSnapshot(
    string ExchangeId,
    string OwnerParticipantId,
    string TargetParticipantId,
    string Outcome,
    int NetDamage,
    bool TargetDefeated);

public sealed record CombatPendingResponseSnapshot(
    string ExchangeId,
    IReadOnlyList<string> AvailableResponses);

public sealed record CombatViewerActionsSnapshot(
    Guid? ActorCharacterId,
    bool CanMeleeAttack,
    bool CanPass,
    IReadOnlyList<string> EligibleTargetParticipantIds,
    CombatPendingResponseSnapshot? PendingResponse);

public sealed record CombatSnapshot(
    bool Active,
    int Round,
    string? CurrentActorParticipantId,
    IReadOnlyList<CombatParticipantSnapshot> Participants,
    CombatExchangeSnapshot? LastExchange,
    CombatPendingSnapshot? Pending,
    CombatDamageSnapshot? LastDamage = null,
    CombatViewerActionsSnapshot? ViewerActions = null);

public sealed record ApplyDamageCommand(
    Guid RoomId,
    Guid CharacterId,
    string EventKey,
    int Damage,
    int? ConRoll = null);

public sealed record HpDamageResult(GameSnapshot Snapshot, HpDamageEvent? Event, bool Deduped);

public sealed record ResolveDyingRoundCommand(Guid RoomId, Guid CharacterId, string? SourceId);

public sealed record ResolveFirstAidCommand(Guid RoomId, Guid CharacterId, int Target, bool WithinHour, string? SourceId);

public sealed record HealthStabilizationResult(
    GameSnapshot Snapshot,
    DyingCheckRecord? DyingCheck,
    TreatmentRecord? Treatment);

public sealed record GameError(GameErrorCode Code);

public enum GameErrorCode
{
    RoomNotFound,
    RoomClosed,
    NotMember,
    NotHost,
    InvalidRoster,
    UnknownPlayer,
    DuplicateCharacterOwnership,
    AlreadyInitialized,
    GameNotFound,
    CharacterNotFound,
    CharacterNotOwned,
    InvalidCheckKey,
    InvalidCheckRequest,
    InvalidHealthSetup,
    InvalidDamage,
    InvalidHealthStabilization,
    InvalidCombat,
    InvalidParticipant,
    InvalidResponse,
    PendingConflict,
    EndedCombat,
    InvalidExchange,
    StateConflict
}

public sealed class GameResult<T>
{
    private GameResult(T? value, GameError? error, bool changed)
    {
        Value = value;
        Error = error;
        Changed = changed;
    }

    public T? Value { get; }

    public GameError? Error { get; }

    public bool IsSuccess => Error is null;

    public bool Changed { get; }

    public static GameResult<T> Success(T value, bool changed = true) => new(value, null, changed);

    public static GameResult<T> Failure(GameErrorCode code) => new(default, new GameError(code), false);
}
