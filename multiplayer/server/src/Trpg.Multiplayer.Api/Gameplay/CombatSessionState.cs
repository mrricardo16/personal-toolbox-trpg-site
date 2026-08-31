namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record CombatParticipantId(string Value);

public sealed record CombatParticipantState(
    CombatParticipantId ParticipantId,
    Guid? CharacterId,
    Guid? OwnerPlayerId,
    string Label,
    string Kind,
    string Side,
    int Dex,
    int Fighting,
    int Dodge,
    IReadOnlyList<CombatResponse> AvailableResponses,
    int ResponseAllowance,
    bool Active);

public sealed record DamageDisposition(
    string ExchangeId,
    CombatParticipantId OwnerParticipantId,
    CombatParticipantId TargetParticipantId,
    string Mode,
    bool Pending,
    bool HpCommitted);

public sealed record PendingCombatExchange(
    string ExchangeId,
    int Round,
    int TurnIndex,
    CombatParticipantId AttackerParticipantId,
    CombatParticipantId DefenderParticipantId,
    Guid? DefenderOwnerPlayerId,
    IReadOnlyList<CombatResponse> AvailableResponses,
    int ResponseCountBefore,
    long CreatedRevision,
    DateTimeOffset CreatedAt);

public sealed record CombatExchange(
    string ExchangeId,
    int Round,
    int TurnIndex,
    CombatParticipantId AttackerParticipantId,
    CombatParticipantId DefenderParticipantId,
    CombatResponse Response,
    CheckResolutionResult AttackerCheck,
    CheckResolutionResult DefenderCheck,
    int OutnumberedBonusDice,
    int ResponseCountBefore,
    int ResponseAllowance,
    string Outcome,
    CombatParticipantId? WinnerParticipantId,
    DamageDisposition? DamageDisposition,
    DateTimeOffset CreatedAt);

public sealed record DyingScheduleState(int ObservedRound, int? LastCheckCompletedRound);

public sealed record CombatSession(
    Guid CombatId,
    bool Active,
    int Round,
    int TurnIndex,
    IReadOnlyList<CombatParticipantId> Order,
    IReadOnlyList<CombatParticipantState> Participants,
    IReadOnlyDictionary<string, int> ResponseCounts,
    IReadOnlyDictionary<string, int> ActionCounts,
    PendingCombatExchange? PendingExchange,
    CombatExchange? LastExchange,
    IReadOnlyList<CombatExchange> History,
    IReadOnlyDictionary<string, DamageDisposition> PendingDamageDispositions,
    IReadOnlyDictionary<Guid, DyingScheduleState> DyingSchedule,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? EndReason);

public static class CombatSessionState
{
    public static CombatParticipantState CreateParticipant(
        CombatParticipantId participantId,
        Guid? characterId,
        Guid? ownerPlayerId,
        string label,
        string kind,
        string side,
        int dex,
        int fighting,
        int dodge,
        IEnumerable<CombatResponse> availableResponses,
        int responseAllowance,
        bool active) => new(
            participantId,
            characterId,
            ownerPlayerId,
            label,
            kind,
            side,
            dex,
            fighting,
            dodge,
            NormalizeResponses(availableResponses, allowEmpty: true),
            responseAllowance,
            active);

    public static PendingCombatExchange CreatePendingExchange(
        string exchangeId,
        int round,
        int turnIndex,
        CombatParticipantId attackerParticipantId,
        CombatParticipantId defenderParticipantId,
        Guid? defenderOwnerPlayerId,
        IEnumerable<CombatResponse> availableResponses,
        int responseCountBefore,
        long createdRevision,
        DateTimeOffset createdAt) => new(
            exchangeId,
            round,
            turnIndex,
            attackerParticipantId,
            defenderParticipantId,
            defenderOwnerPlayerId,
            NormalizeResponses(availableResponses, allowEmpty: false),
            responseCountBefore,
            createdRevision,
            createdAt);

    private static IReadOnlyList<CombatResponse> NormalizeResponses(
        IEnumerable<CombatResponse> availableResponses,
        bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(availableResponses);
        var normalized = availableResponses.Distinct().ToArray();
        if (!allowEmpty && normalized.Length == 0)
        {
            throw new ArgumentException("A pending combat exchange requires at least one response.", nameof(availableResponses));
        }

        return Array.AsReadOnly(normalized);
    }
}
