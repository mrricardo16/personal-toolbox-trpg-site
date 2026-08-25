using System.Text.Json;
using Trpg.Multiplayer.Api.Gameplay;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class HealthStabilizationResolutionTests
{
    private static readonly DateTimeOffset FixedTime = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CocEngine_ConsumesEveryCommittedSinglePlayerHealthStabilizationFixtureCase()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "health-stabilization.json");
        var fixture = JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), JsonOptions);

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.Version);
        Assert.Contains("src/hp-damage-state.js", fixture.ReferenceSources);
        Assert.Contains("src/health-stabilization.js", fixture.ReferenceSources);
        Assert.NotEmpty(fixture.Cases);

        IHealthStabilizationEngine engine = new CocHealthStabilizationEngine();
        IHpDamageEngine hpEngine = new CocHpDamageEngine();
        foreach (var testCase in fixture.Cases)
        {
            var state = testCase.Initial.ToDomain();
            foreach (var operation in testCase.Operations)
            {
                var actual = operation.TryResolve(engine, hpEngine, state);
                AssertOperationOutcome(operation, actual, testCase.TimestampsNonSemantic);
                state = actual.State;
            }

            AssertSemanticState(testCase.Expected, state, testCase.TimestampsNonSemantic);
        }
    }

    [Fact]
    public void DyingRound_DerivesConTargetAndOrdinalFromState()
    {
        var state = Health(0, 12, 60, dying: true);
        var result = new CocHealthStabilizationEngine().ResolveDyingRound(
            state,
            new DyingRoundInput(60, "dying-1", FixedTime));

        Assert.True(result.Changed);
        Assert.True(result.DyingCheck!.Success);
        Assert.Equal(1, result.DyingCheck.Ordinal);
        Assert.Equal(60, result.DyingCheck.Target);
        Assert.Equal(2, result.State.DyingEpisode!.NextRoundOrdinal);
        Assert.Null(result.State.Stabilized);
    }

    [Fact]
    public void DyingRound_FailureCreatesDeadConditionAndClearsActiveConditions()
    {
        var state = Health(0, 12, 60, majorWound: true, unconscious: true, dying: true);
        var result = new CocHealthStabilizationEngine().ResolveDyingRound(
            state,
            new DyingRoundInput(61, "dying-failure", FixedTime));

        Assert.False(result.DyingCheck!.Success);
        Assert.Equal("dying_con_failure", result.State.DeadCondition!.Reason);
        Assert.Null(result.State.DyingEpisode);
        Assert.Null(result.State.Stabilized);
        Assert.False(result.State.Unconscious);
    }

    [Fact]
    public void DyingRound_SecondSuccessUsesOrdinalTwo()
    {
        var state = Health(0, 12, 60, dying: true);
        var engine = new CocHealthStabilizationEngine();

        var first = engine.ResolveDyingRound(state, new DyingRoundInput(60, "round-1", FixedTime));
        var second = engine.ResolveDyingRound(first.State, new DyingRoundInput(1, "round-2", FixedTime));

        Assert.Equal(2, second.DyingCheck!.Ordinal);
        Assert.Equal(2, second.State.DyingEpisode!.Checks.Count);
        Assert.True(second.State.Dying);
    }

    [Fact]
    public void FirstAid_RejectsInvalidTargetOutsideHourDeadAndUntreatableState()
    {
        var engine = new CocHealthStabilizationEngine();
        Assert.Equal(
            HealthStabilizationError.InvalidTarget,
            Assert.Throws<HealthStabilizationRuleException>(() => engine.ResolveFirstAid(
                Health(8, 12, 60), new FirstAidInput(0, true, 1, null, FixedTime))).Error);
        Assert.Equal(
            HealthStabilizationError.FirstAidOutsideHour,
            Assert.Throws<HealthStabilizationRuleException>(() => engine.ResolveFirstAid(
                Health(8, 12, 60), new FirstAidInput(60, false, 1, null, FixedTime))).Error);
        Assert.Equal(
            HealthStabilizationError.NoTreatableInjury,
            Assert.Throws<HealthStabilizationRuleException>(() => engine.ResolveFirstAid(
                Health(12, 12, 60), new FirstAidInput(60, true, 1, null, FixedTime))).Error);
        Assert.Equal(
            HealthStabilizationError.NoTreatableInjury,
            Assert.Throws<HealthStabilizationRuleException>(() => engine.ResolveFirstAid(
                Health(0, 12, 60, dead: true), new FirstAidInput(60, true, 1, null, FixedTime))).Error);
    }

    [Fact]
    public void DyingRound_RejectsMissingActiveEpisode()
    {
        var exception = Assert.Throws<HealthStabilizationRuleException>(() =>
            new CocHealthStabilizationEngine().ResolveDyingRound(
                Health(0, 12, 60),
                new DyingRoundInput(1, null, FixedTime)));

        Assert.Equal(HealthStabilizationError.NoActiveDying, exception.Error);
    }

    [Fact]
    public void FirstAid_SuccessCapsHpWakesCharacterAndPreservesMajorWound()
    {
        var result = new CocHealthStabilizationEngine().ResolveFirstAid(
            Health(12, 12, 60, majorWound: true, unconscious: true),
            new FirstAidInput(60, true, 1, "first-aid", FixedTime));

        Assert.Equal(12, result.State.CurrentHp);
        Assert.False(result.State.Unconscious);
        Assert.True(result.State.MajorWound);
        Assert.Equal(0, result.Treatment!.HealedHp);
        Assert.True(result.Treatment.RousedUnconscious);
    }

    [Fact]
    public void FirstAid_DyingSuccessStabilizesAndFailedFirstAidDoesNotHeal()
    {
        var engine = new CocHealthStabilizationEngine();
        var dying = Health(0, 12, 60, majorWound: true, unconscious: true, dying: true);

        var failed = engine.ResolveFirstAid(dying, new FirstAidInput(60, true, 61, "failed", FixedTime));
        Assert.Equal(0, failed.State.CurrentHp);
        Assert.True(failed.State.Dying);
        Assert.Null(failed.State.Stabilized);
        Assert.False(failed.Treatment!.Success);
        Assert.Single(failed.State.TreatmentHistory);

        var success = engine.ResolveFirstAid(dying, new FirstAidInput(60, true, 60, "success", FixedTime));
        Assert.Equal(1, success.State.CurrentHp);
        Assert.False(success.State.Dying);
        Assert.True(success.State.Stabilized is not null);
        Assert.True(success.Treatment!.StabilizedDying);
        Assert.True(success.State.MajorWound);
    }

    [Fact]
    public void DyingRound_RejectsInvalidRollAndDeadState()
    {
        var engine = new CocHealthStabilizationEngine();
        Assert.Equal(
            HealthStabilizationError.InvalidRoll,
            Assert.Throws<HealthStabilizationRuleException>(() => engine.ResolveDyingRound(
                Health(0, 12, 60, dying: true), new DyingRoundInput(101, null, FixedTime))).Error);
        Assert.Equal(
            HealthStabilizationError.AlreadyDead,
            Assert.Throws<HealthStabilizationRuleException>(() => engine.ResolveDyingRound(
                Health(0, 12, 60, dead: true), new DyingRoundInput(1, null, FixedTime))).Error);
    }

    private static CharacterHealthState Health(
        int currentHp,
        int maxHp,
        int con,
        bool majorWound = false,
        bool unconscious = false,
        bool dying = false,
        bool stabilized = false,
        bool dead = false)
    {
        return new CharacterHealthState(
            currentHp,
            maxHp,
            con,
            majorWound,
            unconscious,
            dying ? new DyingEpisodeState("damage-1", [], 1, true) : null,
            stabilized ? new StabilizedConditionState("first-aid", "first_aid", 60, 1, FixedTime) : null,
            dead ? new DeadConditionState("damage-1", "instant_death", FixedTime) : null,
            [],
            [],
            null);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static void AssertOperationOutcome(
        FixtureOperation operation,
        OperationResolution actual,
        bool timestampsNonSemantic)
    {
        Assert.Equal(operation.Expected.Changed, actual.Changed);
        Assert.Equal(operation.Expected.Error, actual.Error?.ToString());

        if (operation.Expected.DyingCheck is not null)
        {
            Assert.NotNull(actual.DyingCheck);
            AssertDyingCheck(operation.Expected.DyingCheck, actual.DyingCheck!, timestampsNonSemantic);
        }
        else
        {
            Assert.Null(actual.DyingCheck);
        }

        if (operation.Expected.Treatment is not null)
        {
            Assert.NotNull(actual.Treatment);
            AssertTreatment(operation.Expected.Treatment, actual.Treatment!, timestampsNonSemantic);
        }
        else
        {
            Assert.Null(actual.Treatment);
        }

        AssertDamageEvent(operation.Expected.DamageEvent, actual.DamageEvent);
        AssertSemanticState(operation.Expected.State, actual.State, timestampsNonSemantic);
    }

    private static void AssertSemanticState(FixtureHealthState expected, CharacterHealthState actual, bool timestampsNonSemantic)
    {
        Assert.Equal(expected.CurrentHp, actual.CurrentHp);
        Assert.Equal(expected.MaxHp, actual.MaxHp);
        Assert.Equal(expected.Con, actual.Con);
        Assert.Equal(expected.MajorWound, actual.MajorWound);
        Assert.Equal(expected.Unconscious, actual.Unconscious);
        Assert.Equal(expected.DyingEpisode is not null, actual.DyingEpisode is not null);
        Assert.Equal(expected.Stabilized is not null, actual.Stabilized is not null);
        Assert.Equal(expected.Dead is not null, actual.DeadCondition is not null);
        Assert.Equal(expected.TreatmentHistory.Count, actual.TreatmentHistory.Count);
        Assert.InRange(actual.TreatmentHistory.Count, 0, CharacterHealthState.TreatmentHistoryLimit);
        Assert.Equal(expected.History.Count, actual.History.Count);
        AssertDamageEvent(expected.LastEvent, actual.LastDamageEvent);

        foreach (var pair in expected.History.Zip(actual.History))
        {
            AssertDamageEvent(pair.First, pair.Second);
        }

        if (expected.DyingEpisode is not null)
        {
            Assert.NotNull(actual.DyingEpisode);
            Assert.Equal(expected.DyingEpisode.SourceEventKey, actual.DyingEpisode!.SourceEventKey);
            Assert.Equal(expected.DyingEpisode.NextRoundOrdinal, actual.DyingEpisode.NextRoundOrdinal);
            Assert.Equal(expected.DyingEpisode.RoundChecksManaged, actual.DyingEpisode.RoundChecksManaged);
            Assert.Equal(expected.DyingEpisode.Checks.Count, actual.DyingEpisode.Checks.Count);
            Assert.InRange(actual.DyingEpisode.Checks.Count, 0, CharacterHealthState.DyingCheckHistoryLimit);
            foreach (var pair in expected.DyingEpisode.Checks.Zip(actual.DyingEpisode.Checks))
            {
                AssertDyingCheck(pair.First, pair.Second, timestampsNonSemantic);
            }
        }

        if (expected.Stabilized is not null)
        {
            Assert.NotNull(actual.Stabilized);
            Assert.Equal(expected.Stabilized.SourceId, actual.Stabilized!.SourceId);
            Assert.Equal(expected.Stabilized.Reason, actual.Stabilized.Reason);
            Assert.Equal(expected.Stabilized.FirstAidTarget, actual.Stabilized.FirstAidTarget);
            Assert.Equal(expected.Stabilized.FirstAidRoll, actual.Stabilized.FirstAidRoll);
            AssertTimestamp(expected.Stabilized.OccurredAt, actual.Stabilized.OccurredAt, timestampsNonSemantic);
        }

        if (expected.Dead is not null)
        {
            Assert.NotNull(actual.DeadCondition);
            Assert.Equal(expected.Dead.SourceEventKey, actual.DeadCondition!.SourceEventKey);
            Assert.Equal(expected.Dead.Reason, actual.DeadCondition.Reason);
            AssertTimestamp(expected.Dead.OccurredAt, actual.DeadCondition.OccurredAt, timestampsNonSemantic);
        }

        foreach (var pair in expected.TreatmentHistory.Zip(actual.TreatmentHistory))
        {
            AssertTreatment(pair.First, pair.Second, timestampsNonSemantic);
        }
    }

    private static void AssertDyingCheck(FixtureDyingCheck expected, DyingCheckRecord actual, bool timestampsNonSemantic)
    {
        Assert.Equal(expected.Ordinal, actual.Ordinal);
        Assert.Equal(expected.Roll, actual.Roll);
        Assert.Equal(expected.Target, actual.Target);
        Assert.Equal(expected.Success, actual.Success);
        Assert.Equal(expected.SourceId, actual.SourceId);
        AssertTimestamp(expected.OccurredAt, actual.OccurredAt, timestampsNonSemantic);
    }

    private static void AssertTreatment(FixtureTreatment expected, TreatmentRecord actual, bool timestampsNonSemantic)
    {
        Assert.Equal(expected.SourceId, actual.SourceId);
        Assert.Equal(expected.Target, actual.Target);
        Assert.Equal(expected.Roll, actual.Roll);
        Assert.Equal(expected.Success, actual.Success);
        Assert.Equal(expected.WithinHour, actual.WithinHour);
        Assert.Equal(expected.HpBefore, actual.HpBefore);
        Assert.Equal(expected.HpAfter, actual.HpAfter);
        Assert.Equal(expected.HealedHp, actual.HealedHp);
        Assert.Equal(expected.WasDying, actual.WasDying);
        Assert.Equal(expected.WasUnconscious, actual.WasUnconscious);
        Assert.Equal(expected.StabilizedDying, actual.StabilizedDying);
        Assert.Equal(expected.RousedUnconscious, actual.RousedUnconscious);
        AssertTimestamp(expected.OccurredAt, actual.OccurredAt, timestampsNonSemantic);
    }

    private static void AssertDamageEvent(FixtureEvent? expected, HpDamageEvent? actual)
    {
        if (expected is null)
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        Assert.Equal(expected.EventKey, actual!.EventKey);
        Assert.Equal(expected.Damage, actual.Damage);
        Assert.Equal(expected.MajorWound, actual.MajorWound);
        Assert.Equal(expected.InstantDeath, actual.InstantDeath);
        Assert.Equal(expected.ConCheck?.Roll, actual.ConCheck?.Roll);
        Assert.Equal(expected.ConCheck?.Target, actual.ConCheck?.Target);
        Assert.Equal(expected.ConCheck?.Success, actual.ConCheck?.Success);
    }

    private static void AssertTimestamp(DateTimeOffset? expected, DateTimeOffset? actual, bool timestampsNonSemantic)
    {
        if (!timestampsNonSemantic)
        {
            Assert.Equal(expected, actual);
        }
    }

    private sealed record FixtureDocument(int Version, IReadOnlyList<string> ReferenceSources, IReadOnlyList<FixtureCase> Cases);

    private sealed record FixtureCase(
        string Name,
        bool TimestampsNonSemantic,
        FixtureHealthState Initial,
        IReadOnlyList<FixtureOperation> Operations,
        FixtureHealthState Expected);

    private sealed record FixtureOperation(
        string Kind,
        int? Roll,
        int? Target,
        bool? WithinHour,
        string? SourceId,
        DateTimeOffset? OccurredAt,
        string? EventKey,
        int? Damage,
        FixtureOperationExpected Expected)
    {
        public OperationResolution TryResolve(IHealthStabilizationEngine engine, IHpDamageEngine hpEngine, CharacterHealthState state)
        {
            try
            {
                if (Kind == "damage")
                {
                    var damageResult = hpEngine.Apply(state, new HpDamageInput(EventKey!, Damage!.Value, Roll));
                    return new OperationResolution(damageResult.State, damageResult.Changed, null, null, damageResult.Event, null);
                }

                var result = Kind switch
                {
                    "dyingRound" => engine.ResolveDyingRound(state, new DyingRoundInput(Roll!.Value, SourceId, OccurredAt)),
                    "firstAid" => engine.ResolveFirstAid(state, new FirstAidInput(Target!.Value, WithinHour!.Value, Roll!.Value, SourceId, OccurredAt)),
                    _ => throw new InvalidOperationException($"Unknown fixture operation '{Kind}'.")
                };
                return new OperationResolution(result.State, result.Changed, result.DyingCheck, result.Treatment, null, null);
            }
            catch (HealthStabilizationRuleException exception)
            {
                return new OperationResolution(state, false, null, null, null, exception.Error);
            }
        }
    }

    private sealed record OperationResolution(
        CharacterHealthState State,
        bool Changed,
        DyingCheckRecord? DyingCheck,
        TreatmentRecord? Treatment,
        HpDamageEvent? DamageEvent,
        HealthStabilizationError? Error);

    private sealed record FixtureOperationExpected(
        bool Changed,
        string? Error,
        FixtureDyingCheck? DyingCheck,
        FixtureTreatment? Treatment,
        FixtureEvent? DamageEvent,
        FixtureHealthState State);

    private sealed record FixtureHealthState(
        int CurrentHp,
        int MaxHp,
        int Con,
        bool MajorWound,
        bool Unconscious,
        FixtureDyingEpisode? DyingEpisode,
        FixtureStabilized? Stabilized,
        FixtureDead? Dead,
        IReadOnlyList<FixtureTreatment> TreatmentHistory,
        IReadOnlyList<FixtureEvent> History,
        FixtureEvent? LastEvent)
    {
        public CharacterHealthState ToDomain() => new(
            CurrentHp,
            MaxHp,
            Con,
            MajorWound,
            Unconscious,
            DyingEpisode?.ToDomain(),
            Stabilized?.ToDomain(),
            Dead?.ToDomain(),
            TreatmentHistory.Select(item => item.ToDomain()).ToArray(),
            History.Select(item => item.ToDomain()).ToArray(),
            LastEvent?.ToDomain());
    }

    private sealed record FixtureEvent(
        string EventKey,
        int Damage,
        bool MajorWound,
        bool InstantDeath,
        FixtureConCheck? ConCheck)
    {
        public HpDamageEvent ToDomain() => new(
            EventKey,
            Damage,
            MajorWound,
            InstantDeath,
            ConCheck is null ? null : new HpConCheck(ConCheck.Roll, ConCheck.Target, ConCheck.Success));
    }

    private sealed record FixtureConCheck(int Roll, int Target, bool Success);

    private sealed record FixtureDyingEpisode(
        string? SourceEventKey,
        IReadOnlyList<FixtureDyingCheck> Checks,
        int NextRoundOrdinal,
        bool RoundChecksManaged)
    {
        public DyingEpisodeState ToDomain() => new(
            SourceEventKey,
            Checks.Select(item => item.ToDomain()).ToArray(),
            NextRoundOrdinal,
            RoundChecksManaged);
    }

    private sealed record FixtureDyingCheck(int Ordinal, int Roll, int Target, bool Success, string? SourceId, DateTimeOffset? OccurredAt)
    {
        public DyingCheckRecord ToDomain() => new(Ordinal, Roll, Target, Success, SourceId, OccurredAt);
    }

    private sealed record FixtureStabilized(string? SourceId, string Reason, int? FirstAidTarget, int? FirstAidRoll, DateTimeOffset? OccurredAt)
    {
        public StabilizedConditionState ToDomain() => new(SourceId, Reason, FirstAidTarget, FirstAidRoll, OccurredAt);
    }

    private sealed record FixtureDead(string? SourceEventKey, string Reason, DateTimeOffset? OccurredAt)
    {
        public DeadConditionState ToDomain() => new(SourceEventKey, Reason, OccurredAt);
    }

    private sealed record FixtureTreatment(
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
        DateTimeOffset? OccurredAt)
    {
        public TreatmentRecord ToDomain() => new(
            SourceId,
            Target,
            Roll,
            Success,
            WithinHour,
            HpBefore,
            HpAfter,
            HealedHp,
            WasDying,
            WasUnconscious,
            StabilizedDying,
            RousedUnconscious,
            OccurredAt);
    }
}
