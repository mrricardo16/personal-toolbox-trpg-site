using System.Reflection;
using System.Text.Json;
using Trpg.Multiplayer.Api.Gameplay;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class CombatOpposedResolutionTests
{
    [Fact]
    public void DodgeEqualRegular_defenderDodges()
    {
        var result = Resolve(CombatResponse.Dodge, Check("regular"), Check("regular"));

        AssertResolution(result, "defender_dodges", "defender", null);
    }

    [Fact]
    public void DodgeAttackerHigher_attackerHits()
    {
        var result = Resolve(CombatResponse.Dodge, Check("hard"), Check("regular"));

        AssertResolution(result, "attacker_hits", "attacker", "regular");
    }

    [Fact]
    public void DodgeBothFail_noDamageDisposition()
    {
        var result = Resolve(CombatResponse.Dodge, Check("failure"), Check("failure"));

        AssertResolution(result, "both_fail_no_damage", null, null);
    }

    [Fact]
    public void FightBackEqualRegular_attackerWins()
    {
        var result = Resolve(CombatResponse.FightBack, Check("regular"), Check("regular"));

        AssertResolution(result, "attacker_hits", "attacker", "regular");
    }

    [Fact]
    public void FightBackDefenderStrictlyHigher_defenderFightsBack()
    {
        var result = Resolve(CombatResponse.FightBack, Check("regular"), Check("hard"));

        AssertResolution(result, "defender_fights_back", "defender", "fight_back_regular_cap");
    }

    [Fact]
    public void FightBackAttackerFailsDefenderSucceeds_defenderFightsBack()
    {
        var result = Resolve(CombatResponse.FightBack, Check("failure"), Check("regular"));

        AssertResolution(result, "defender_fights_back", "defender", "fight_back_regular_cap");
    }

    [Fact]
    public void FightBackBothFail_noDamageDisposition()
    {
        var result = Resolve(CombatResponse.FightBack, Check("failure"), Check("failure"));

        AssertResolution(result, "both_fail_no_damage", null, null);
    }

    [Fact]
    public void ExtremeAttacker_usesInitiatorExtremeEligible()
    {
        var result = Resolve(CombatResponse.Dodge, Check("extreme"), Check("regular"));

        AssertResolution(result, "attacker_hits", "attacker", "initiator_extreme_eligible");
    }

    [Fact]
    public void ExtremeFightBack_defenderUsesRegularCap()
    {
        var result = Resolve(CombatResponse.FightBack, Check("regular"), Check("extreme"));

        AssertResolution(result, "defender_fights_back", "defender", "fight_back_regular_cap");
    }

    [Fact]
    public void InvalidResponse_rejected()
    {
        var exception = Assert.Throws<CombatOpposedRuleException>(() =>
            Resolve((CombatResponse)999, Check("regular"), Check("regular")));

        Assert.Equal(CombatOpposedError.InvalidResponse, exception.Error);
    }

    [Fact]
    public void CocEngine_ConsumesEveryCombatOpposedFixtureCase()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "combat-opposed.json");
        Assert.True(File.Exists(path), "Task 4 must generate the combat-opposed fixture from the browser VM before this conformance test can run.");

        var fixture = JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), JsonOptions);

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.Version);
        Assert.Equal(RequiredReferenceSources, fixture.ReferenceSources);
        Assert.Equal(RequiredCaseNames, fixture.Cases.Select(testCase => testCase.Name).ToArray());
        Assert.Equal(11, fixture.Cases.Count(testCase => testCase.Scope == "reference_conformance"));
        Assert.Equal(8, fixture.Cases.Count(testCase => testCase.Scope == "multiplayer_generalization"));
        Assert.Equal(RequiredReferenceCaseNames, fixture.Cases.Where(testCase => testCase.Scope == "reference_conformance").Select(testCase => testCase.Name).ToArray());
        Assert.Equal(RequiredGeneralizationCaseNames, fixture.Cases.Where(testCase => testCase.Scope == "multiplayer_generalization").Select(testCase => testCase.Name).ToArray());

        Assert.All(
            fixture.Cases,
            testCase => Assert.Contains(testCase.Scope, new[] { "reference_conformance", "multiplayer_generalization" }));

        foreach (var testCase in fixture.Cases.Where(testCase => testCase.Scope == "reference_conformance"))
        {
            AssertFixtureCase(testCase);
        }
    }

    [Fact]
    public void CombatOpposedResolution_hasNoHpFieldsOrMutation()
    {
        var attacker = Check("hard");
        var defender = Check("regular");
        var input = new CombatOpposedResolutionInput(attacker, defender, CombatResponse.Dodge);

        var result = new CocCombatOpposedEngine().Resolve(input);

        Assert.Equal(attacker, input.AttackerCheck);
        Assert.Equal(defender, input.DefenderCheck);
        AssertNoHpMembers(typeof(CombatOpposedResolutionInput));
        AssertNoHpMembers(typeof(CombatOpposedResolutionResult));
        AssertNoHpMembers(result.GetType());
    }

    private static readonly string[] RequiredReferenceSources =
    [
        "src/check-engine.js",
        "src/coc-resolution-engine.js",
        "src/hp-damage-state.js",
        "src/health-stabilization.js",
        "src/combat-opposed.js"
    ];

    private static readonly string[] RequiredCaseNames =
    [
        "participant-normalization",
        "invalid-opponent-stat",
        "stable-dex-order",
        "equal-dex-input-order",
        "dodge-equal-regular",
        "dodge-attacker-higher",
        "dodge-both-fail",
        "fight-back-equal-regular",
        "fight-back-defender-higher",
        "fight-back-attacker-failure-defender-success",
        "fight-back-both-fail",
        "initiator-extreme-eligibility",
        "fight-back-regular-cap",
        "response-allowance-outnumbered",
        "pass-turn-and-round-wrap",
        "no-winning-disposition-is-null",
        "winning-disposition-is-pending",
        "active-combat-dying-observation",
        "first-following-eligible-dying-round"
    ];

    private static readonly string[] RequiredReferenceCaseNames =
    [
        "dodge-equal-regular",
        "dodge-attacker-higher",
        "dodge-both-fail",
        "fight-back-equal-regular",
        "fight-back-defender-higher",
        "fight-back-attacker-failure-defender-success",
        "fight-back-both-fail",
        "initiator-extreme-eligibility",
        "fight-back-regular-cap",
        "no-winning-disposition-is-null",
        "winning-disposition-is-pending"
    ];

    private static readonly string[] RequiredGeneralizationCaseNames =
    [
        "participant-normalization",
        "invalid-opponent-stat",
        "stable-dex-order",
        "equal-dex-input-order",
        "response-allowance-outnumbered",
        "pass-turn-and-round-wrap",
        "active-combat-dying-observation",
        "first-following-eligible-dying-round"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static CombatOpposedResolutionResult Resolve(CombatResponse response, CheckResolutionResult attacker, CheckResolutionResult defender) =>
        new CocCombatOpposedEngine().Resolve(new CombatOpposedResolutionInput(attacker, defender, response));

    private static CheckResolutionResult Check(string successLevel)
    {
        var passed = CocCheckResolutionRules.SuccessLevelRank(successLevel) > 0;
        return new CheckResolutionResult(
            Roll: 50,
            Target: 60,
            Difficulty: "regular",
            DifficultyTarget: 60,
            SuccessLevel: successLevel,
            Passed: passed,
            Critical: successLevel == "critical",
            Fumble: successLevel == "fumble");
    }

    private static void AssertFixtureCase(FixtureCase testCase)
    {
        Assert.Equal(
            testCase.Expected.AttackerRollRank,
            Rank(testCase.Input.Attacker));
        Assert.Equal(
            testCase.Expected.DefenderRollRank,
            Rank(testCase.Input.Defender));

        try
        {
            Assert.NotNull(testCase.Expected.Outcome);
            var result = Resolve(
                ParseResponse(testCase.Input.Response),
                testCase.Input.Attacker.ToDomain(),
                testCase.Input.Defender.ToDomain());

            Assert.Null(testCase.Expected.Error);
            AssertResolution(
                result,
                testCase.Expected.Outcome,
                testCase.Expected.WinnerSide,
                testCase.Expected.DamageMode);
            AssertWinningDisposition(testCase, result);
        }
        catch (CombatOpposedRuleException exception)
        {
            Assert.Equal(testCase.Expected.Error, exception.Error.ToString());
            Assert.Null(testCase.Expected.DamageDisposition);
        }
    }

    private static CombatResponse ParseResponse(string response) =>
        response.ToLowerInvariant() switch
        {
            "dodge" => CombatResponse.Dodge,
            "fight_back" => CombatResponse.FightBack,
            _ => (CombatResponse)999
        };

    private static int Rank(FixtureCheck check) =>
        CocCheckResolutionRules.SuccessLevelRank(check.SuccessLevel);

    private static void AssertResolution(
        CombatOpposedResolutionResult actual,
        string outcome,
        string? winner,
        string? damageMode)
    {
        Assert.Equal(outcome, actual.Outcome);
        Assert.Equal(winner, actual.WinnerSide);
        Assert.Equal(damageMode, actual.DamageMode);
    }

    private static void AssertWinningDisposition(FixtureCase testCase, CombatOpposedResolutionResult result)
    {
        if (result.DamageMode is null)
        {
            Assert.Null(testCase.Expected.DamageDisposition);
            return;
        }

        Assert.NotNull(testCase.Expected.DamageDisposition);
        var disposition = testCase.Expected.DamageDisposition!;
        var expectedOwner = result.WinnerSide == "attacker"
            ? testCase.Input.AttackerId
            : testCase.Input.DefenderId;
        var expectedTarget = result.WinnerSide == "attacker"
            ? testCase.Input.DefenderId
            : testCase.Input.AttackerId;

        Assert.Equal(expectedOwner, disposition.OwnerId);
        Assert.Equal(expectedTarget, disposition.TargetId);
        Assert.Equal(result.DamageMode, disposition.Mode);
        Assert.True(disposition.Pending);
        Assert.False(disposition.HpCommitted);
    }

    private static string[] PublicHpMembers(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public)
            .Select(field => field.Name)
            .Concat(type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name))
            .Where(name => name.Contains("hp", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private static void AssertNoHpMembers(Type type) => Assert.Empty(PublicHpMembers(type));

    private sealed record FixtureDocument(
        int Version,
        IReadOnlyList<string> ReferenceSources,
        IReadOnlyList<FixtureCase> Cases);

    private sealed record FixtureCase(
        string Name,
        string Scope,
        FixtureInput Input,
        FixtureExpected Expected);

    private sealed record FixtureInput(
        string Response,
        string AttackerId,
        string DefenderId,
        FixtureCheck Attacker,
        FixtureCheck Defender);

    private sealed record FixtureCheck(
        int Roll,
        int Target,
        string Difficulty,
        int DifficultyTarget,
        string SuccessLevel,
        bool Passed,
        bool Critical,
        bool Fumble)
    {
        public CheckResolutionResult ToDomain() => new(
            Roll,
            Target,
            Difficulty,
            DifficultyTarget,
            SuccessLevel,
            Passed,
            Critical,
            Fumble);
    }

    private sealed record FixtureExpected(
        string? Outcome,
        string? WinnerSide,
        string? DamageMode,
        int AttackerRollRank,
        int DefenderRollRank,
        string? Error,
        FixtureDisposition? DamageDisposition);

    private sealed record FixtureDisposition(
        string OwnerId,
        string TargetId,
        string Mode,
        bool Pending,
        bool HpCommitted);
}
