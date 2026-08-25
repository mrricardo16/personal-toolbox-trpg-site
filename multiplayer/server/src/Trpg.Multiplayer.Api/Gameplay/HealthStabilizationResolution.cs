namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record DyingCheckRecord(
    int Ordinal,
    int Roll,
    int Target,
    bool Success,
    string? SourceId,
    DateTimeOffset? OccurredAt);

public sealed record DyingEpisodeState(
    string? SourceEventKey,
    IReadOnlyList<DyingCheckRecord> Checks,
    int NextRoundOrdinal,
    bool RoundChecksManaged);

public sealed record StabilizedConditionState(
    string? SourceId,
    string Reason,
    int? FirstAidTarget,
    int? FirstAidRoll,
    DateTimeOffset? OccurredAt);

public sealed record DeadConditionState(
    string? SourceEventKey,
    string Reason,
    DateTimeOffset? OccurredAt);

public sealed record TreatmentRecord(
    string? SourceId,
    int Target,
    int Roll,
    bool Success,
    bool WithinHour,
    int HpBefore,
    int HpAfter,
    int HealedHp,
    bool WasDying,
    bool WasUnconscious,
    bool StabilizedDying,
    bool RousedUnconscious,
    DateTimeOffset? OccurredAt);

public sealed record DyingRoundInput(
    int Roll,
    string? SourceId,
    DateTimeOffset? OccurredAt);

public sealed record FirstAidInput(
    int Target,
    bool WithinHour,
    int Roll,
    string? SourceId,
    DateTimeOffset? OccurredAt);

public enum HealthStabilizationError
{
    AlreadyDead,
    NoActiveDying,
    NoTreatableInjury,
    InvalidRoll,
    InvalidTarget,
    FirstAidOutsideHour
}

public sealed class HealthStabilizationRuleException : InvalidOperationException
{
    public HealthStabilizationRuleException(HealthStabilizationError error)
        : base(error.ToString())
    {
        Error = error;
    }

    public HealthStabilizationError Error { get; }
}

public sealed record HealthStabilizationResolutionResult(
    CharacterHealthState State,
    DyingCheckRecord? DyingCheck,
    TreatmentRecord? Treatment,
    bool Changed);

public interface IHealthStabilizationEngine
{
    HealthStabilizationResolutionResult ResolveDyingRound(CharacterHealthState state, DyingRoundInput input);

    HealthStabilizationResolutionResult ResolveFirstAid(CharacterHealthState state, FirstAidInput input);
}

public sealed class CocHealthStabilizationEngine : IHealthStabilizationEngine
{
    public HealthStabilizationResolutionResult ResolveDyingRound(CharacterHealthState state, DyingRoundInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ValidateState(state);
        ValidateRoll(input.Roll);

        if (state.Dead)
        {
            throw new HealthStabilizationRuleException(HealthStabilizationError.AlreadyDead);
        }

        var episode = state.DyingEpisode
            ?? throw new HealthStabilizationRuleException(HealthStabilizationError.NoActiveDying);

        ValidateTarget(state.Con);
        var ordinal = episode.Checks.Count + 1;
        var success = input.Roll <= state.Con;
        var check = new DyingCheckRecord(ordinal, input.Roll, state.Con, success, input.SourceId, input.OccurredAt);

        if (!success)
        {
            var dead = new DeadConditionState(episode.SourceEventKey, "dying_con_failure", input.OccurredAt);
            var next = state with
            {
                DyingEpisode = null,
                Stabilized = null,
                Unconscious = false,
                DeadCondition = dead
            };
            return new HealthStabilizationResolutionResult(next, check, null, true);
        }

        var checks = episode.Checks
            .Concat([check])
            .TakeLast(CharacterHealthState.DyingCheckHistoryLimit)
            .ToArray();
        var nextEpisode = episode with
        {
            Checks = checks,
            NextRoundOrdinal = ordinal + 1,
            RoundChecksManaged = true
        };
        var successState = state with
        {
            DyingEpisode = nextEpisode,
            Stabilized = null
        };
        return new HealthStabilizationResolutionResult(successState, check, null, true);
    }

    public HealthStabilizationResolutionResult ResolveFirstAid(CharacterHealthState state, FirstAidInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ValidateState(state);
        ValidateTarget(input.Target);
        ValidateRoll(input.Roll);

        if (!input.WithinHour)
        {
            throw new HealthStabilizationRuleException(HealthStabilizationError.FirstAidOutsideHour);
        }

        if (state.Dead || (state.CurrentHp >= state.MaxHp && !state.Unconscious && !state.Dying))
        {
            throw new HealthStabilizationRuleException(HealthStabilizationError.NoTreatableInjury);
        }

        if (state.Dead)
        {
            throw new HealthStabilizationRuleException(HealthStabilizationError.AlreadyDead);
        }

        var wasDying = state.Dying;
        var wasUnconscious = state.Unconscious;
        var hpBefore = state.CurrentHp;
        var success = input.Roll <= input.Target;
        var hpAfter = success ? Math.Min(state.MaxHp, hpBefore + 1) : hpBefore;
        var treatment = new TreatmentRecord(
            input.SourceId,
            input.Target,
            input.Roll,
            success,
            input.WithinHour,
            hpBefore,
            hpAfter,
            hpAfter - hpBefore,
            wasDying,
            wasUnconscious,
            success && wasDying,
            success && wasUnconscious,
            input.OccurredAt);
        var history = state.TreatmentHistory
            .Concat([treatment])
            .TakeLast(CharacterHealthState.TreatmentHistoryLimit)
            .ToArray();

        if (!success)
        {
            var failedState = state with { TreatmentHistory = history };
            return new HealthStabilizationResolutionResult(failedState, null, treatment, true);
        }

        var stabilized = wasDying
            ? new StabilizedConditionState(input.SourceId, "successful_first_aid", input.Target, input.Roll, input.OccurredAt)
            : null;
        var next = state with
        {
            CurrentHp = hpAfter,
            Unconscious = false,
            DyingEpisode = null,
            Stabilized = stabilized,
            TreatmentHistory = history
        };
        return new HealthStabilizationResolutionResult(next, null, treatment, true);
    }

    private static void ValidateState(CharacterHealthState state)
    {
        if (state.MaxHp < 1 || state.CurrentHp < 0 || state.CurrentHp > state.MaxHp || state.Con is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Health state is outside the supported HP contract.");
        }

        if (state.Dead && (state.Dying || state.Stabilized is not null || state.Unconscious))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Dead health state contains an active condition.");
        }

        if (state.DyingEpisode is { Checks.Count: > CharacterHealthState.DyingCheckHistoryLimit })
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Dying check history exceeds the supported limit.");
        }

        if (state.TreatmentHistory.Count > CharacterHealthState.TreatmentHistoryLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Treatment history exceeds the supported limit.");
        }
    }

    private static void ValidateRoll(int roll)
    {
        if (roll is < 1 or > 100)
        {
            throw new HealthStabilizationRuleException(HealthStabilizationError.InvalidRoll);
        }
    }

    private static void ValidateTarget(int target)
    {
        if (target is < 1 or > 100)
        {
            throw new HealthStabilizationRuleException(HealthStabilizationError.InvalidTarget);
        }
    }
}
