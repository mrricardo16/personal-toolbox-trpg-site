namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record CombatParticipantId(string Value);

public sealed record CombatDamageProfile(
    int Str,
    int Siz,
    DamageBonusProfile DamageBonus,
    CombatWeaponProfile Weapon,
    int FixedArmor);

public sealed record OpponentVitalityState(int CurrentHp, int MaxHp);

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
    bool Active,
    CombatDamageProfile DamageProfile,
    OpponentVitalityState? OpponentVitality);

public enum DamageDispositionStatus
{
    Pending,
    Consumed
}

public enum CombatDamageOutcome
{
    Applied,
    TargetAlreadyIneligible
}

public sealed record DamageDispositionData(
    string ExchangeId,
    CombatParticipantId OwnerParticipantId,
    CombatParticipantId TargetParticipantId,
    CombatDamageMode Mode,
    long CreatedGameRevision);

public sealed record CombatDamageResult(
    string ExchangeId,
    CombatParticipantId OwnerParticipantId,
    CombatParticipantId TargetParticipantId,
    CombatDamageMode DamageMode,
    CombatDamageOutcome Outcome,
    string? WeaponId,
    string? WeaponExpression,
    DamageComponentResult? WeaponResult,
    DamageComponentResult? DamageBonusResult,
    int GrossDamage,
    int Armor,
    int NetDamage,
    int HpBefore,
    int HpAfter,
    bool HpDamageApplied,
    bool TargetDefeated,
    DateTimeOffset ResolvedAt);

public sealed record DamageDispositionState(
    DamageDispositionData Disposition,
    DamageDispositionStatus Status,
    CombatDamageResult? Result)
{
    // Compatibility projection only; Status remains the sole canonical lifecycle truth.
    public bool Pending => Status == DamageDispositionStatus.Pending;
}

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
    DamageDispositionState? DamageDisposition,
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
    IReadOnlyDictionary<string, DamageDispositionState> DamageDispositions,
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
        bool active,
        CombatDamageProfile damageProfile,
        OpponentVitalityState? opponentVitality) => new(
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
            active,
            damageProfile,
            opponentVitality);

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
