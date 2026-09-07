using System.Text.Json;
using Trpg.Multiplayer.Api.Gameplay;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class CombatDamageResolutionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] RequiredReferenceSources =
    [
        "src/check-engine.js",
        "src/hp-damage-state.js",
        "src/health-stabilization.js",
        "src/combat-opposed.js",
        "src/combat-damage.js"
    ];

    private static readonly string[] RequiredFixtureCaseIds =
    [
        "db-64", "db-65", "db-84", "db-85", "db-124", "db-125",
        "db-164", "db-165", "db-204", "db-205", "db-284", "db-285",
        "dice-d3", "dice-1d3", "dice-1d6-plus-2", "dice-2d4-minus-1",
        "dice-trim-uppercase", "dice-empty", "dice-invalid-format", "dice-count-zero",
        "dice-count-101", "dice-faces-one", "dice-faces-10001",
        "dice-modifier-min", "dice-modifier-below-min", "dice-modifier-max",
        "dice-modifier-above-max", "dice-length-33",
        "weapon-melee-non-impaling", "weapon-unsupported-mode",
        "regular-dice-db", "regular-flat-negative-db", "regular-gross-floor-zero",
        "regular-adds-db-false", "extreme-weapon-modifier-max",
        "extreme-positive-db-max", "extreme-zero-db", "extreme-negative-db",
        "fight-back-regular-cap", "armor-partial", "armor-equal-gross",
        "armor-above-gross", "zero-net-no-hp-event", "investigator-positive-hp",
        "major-wound-after-armor", "instant-death-after-armor",
        "opponent-defeat", "target-already-defeated-reference-issue"
    ];

    private static readonly string[] AllowedScopes =
    [
        "reference_conformance",
        "potential_reference_issue",
        "deferred"
    ];

    [Fact]
    public void DiceExpressionParser_NormalizesTheTaskOnePublicContract()
    {
        var parsed = DiceExpressionParser.Parse("  D6+2 ");

        Assert.Equal(new DiceExpression("d6+2", 1, 6, 2), parsed);
    }

    [Theory]
    [InlineData(65, 60, 125, "1d4")]
    [InlineData(0, 0, 2, "-2")]
    [InlineData(-10, 1, 2, "-2")]
    public void DamageBonusRules_DeriveTheTaskOnePublicContract(
        int str,
        int siz,
        int expectedSum,
        string expectedExpression)
    {
        var bonus = CocCombatDamageRules.DeriveDamageBonus(str, siz);

        Assert.Equal(expectedSum, bonus.Sum);
        Assert.Equal(expectedExpression, bonus.Expression);
    }

    [Fact]
    public void CocCombatDamageEngine_ResolvesTheTaskOnePublicContract()
    {
        var weapon = CocCombatDamageRules.NormalizeWeapon(
            "unarmed",
            "Unarmed",
            "1d6",
            addsDamageBonus: true,
            "melee_non_impaling");
        var bonus = CocCombatDamageRules.DeriveDamageBonus(65, 60);

        var result = new CocCombatDamageEngine().Resolve(new CombatDamageInput(
            Weapon: weapon,
            DamageBonus: bonus,
            Mode: CombatDamageMode.Regular,
            WeaponRoll: new GenericDiceRoll(1, 6, [4], 4),
            DamageBonusRoll: new GenericDiceRoll(1, 4, [3], 3),
            Armor: 2));

        Assert.Equal(5, result.NetDamage);
    }

    [Theory]
    [InlineData(32, 32, 64, DamageBonusKind.Flat, -2, 0, 0, "-2", -2)]
    [InlineData(32, 33, 65, DamageBonusKind.Flat, -1, 0, 0, "-1", -1)]
    [InlineData(42, 42, 84, DamageBonusKind.Flat, -1, 0, 0, "-1", -1)]
    [InlineData(42, 43, 85, DamageBonusKind.Flat, 0, 0, 0, "0", 0)]
    [InlineData(62, 62, 124, DamageBonusKind.Flat, 0, 0, 0, "0", 0)]
    [InlineData(62, 63, 125, DamageBonusKind.Dice, 0, 1, 4, "1d4", 4)]
    [InlineData(82, 82, 164, DamageBonusKind.Dice, 0, 1, 4, "1d4", 4)]
    [InlineData(82, 83, 165, DamageBonusKind.Dice, 0, 1, 6, "1d6", 6)]
    [InlineData(102, 102, 204, DamageBonusKind.Dice, 0, 1, 6, "1d6", 6)]
    [InlineData(102, 103, 205, DamageBonusKind.Dice, 0, 2, 6, "2d6", 12)]
    [InlineData(142, 142, 284, DamageBonusKind.Dice, 0, 2, 6, "2d6", 12)]
    [InlineData(142, 143, 285, DamageBonusKind.Dice, 0, 3, 6, "3d6", 18)]
    [InlineData(182, 182, 364, DamageBonusKind.Dice, 0, 3, 6, "3d6", 18)]
    [InlineData(182, 183, 365, DamageBonusKind.Dice, 0, 4, 6, "4d6", 24)]
    public void DamageBonusRules_UseTheFinalizedBoundaryTable(
        int str,
        int siz,
        int expectedSum,
        DamageBonusKind expectedKind,
        int expectedFlatValue,
        int expectedCount,
        int expectedFaces,
        string expectedExpression,
        int expectedMaximum)
    {
        var actual = CocCombatDamageRules.DeriveDamageBonus(str, siz);

        Assert.Equal(new DamageBonusProfile(
            expectedSum,
            expectedKind,
            expectedFlatValue,
            expectedCount,
            expectedFaces,
            expectedExpression,
            expectedMaximum), actual);
    }

    [Fact]
    public void DamageBonusRules_UseCheckedStrengthAndSizeAddition()
    {
        Assert.Throws<OverflowException>(() =>
            CocCombatDamageRules.DeriveDamageBonus(int.MaxValue, 1));
    }

    [Fact]
    public void NormalizeWeapon_AcceptsOnlyNormalizedNonImpalingMelee()
    {
        var weapon = CocCombatDamageRules.NormalizeWeapon(
            "club",
            "Club",
            " 2D4-1 ",
            addsDamageBonus: false,
            "melee_non_impaling");

        Assert.Equal(new CombatWeaponProfile(
            "club",
            "Club",
            new DiceExpression("2d4-1", 2, 4, -1),
            false,
            "melee_non_impaling"), weapon);
        Assert.Throws<NotSupportedException>(() => CocCombatDamageRules.NormalizeWeapon(
            "spear",
            "Spear",
            "1d8",
            addsDamageBonus: true,
            "impaling"));
    }

    [Fact]
    public void RegularDamage_RollsWeaponAndDiceBonusAndAppliesModifier()
    {
        var result = Resolve(
            expression: "2d4-1",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(142, 143),
            mode: CombatDamageMode.Regular,
            weaponRoll: new GenericDiceRoll(2, 4, [4, 2], 6),
            damageBonusRoll: new GenericDiceRoll(3, 6, [6, 4, 2], 12));

        AssertComponent(result.WeaponResult, "2d4-1", [4, 2], -1, 5, maximized: false);
        AssertComponent(result.DamageBonusResult, "3d6", [6, 4, 2], 0, 12, maximized: false);
        Assert.Equal(17, result.GrossDamage);
        Assert.Equal(17, result.NetDamage);
    }

    [Theory]
    [InlineData(32, 33, -1, 2)]
    [InlineData(0, 0, -2, 1)]
    public void RegularDamage_AppliesFlatNegativeBonusWithoutBonusRoll(
        int str,
        int siz,
        int expectedBonus,
        int expectedGross)
    {
        var result = Resolve(
            expression: "1d4",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(str, siz),
            mode: CombatDamageMode.Regular,
            weaponRoll: new GenericDiceRoll(1, 4, [3], 3));

        Assert.Equal(new DamageComponentResult(
            expectedBonus.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [],
            expectedBonus,
            expectedBonus,
            false), result.DamageBonusResult);
        Assert.Equal(expectedGross, result.GrossDamage);
    }

    [Fact]
    public void RegularDamage_WhenWeaponExcludesBonus_UsesZeroWithoutBonusRoll()
    {
        var result = Resolve(
            expression: "1d6+2",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(182, 183),
            mode: CombatDamageMode.Regular,
            weaponRoll: new GenericDiceRoll(1, 6, [4], 4),
            addsDamageBonus: false);

        Assert.Equal(new DamageComponentResult("0", [], 0, 0, false), result.DamageBonusResult);
        Assert.Equal(6, result.GrossDamage);
    }

    [Fact]
    public void RegularDamage_ClampsNegativeGrossToZero()
    {
        var result = Resolve(
            expression: "1d2-2",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(0, 0),
            mode: CombatDamageMode.Regular,
            weaponRoll: new GenericDiceRoll(1, 2, [1], 1));

        Assert.Equal(-1, result.WeaponResult.Total);
        Assert.Equal(-2, result.DamageBonusResult.Total);
        Assert.Equal(0, result.GrossDamage);
        Assert.Equal(0, result.NetDamage);
    }

    [Fact]
    public void InitiatorExtreme_MaximizesWeaponAndPositiveDiceBonusWithoutRolls()
    {
        var result = Resolve(
            expression: "100d10000+100000",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(182, 183),
            mode: CombatDamageMode.InitiatorExtremeEligible);

        Assert.Equal(new DamageComponentResult(
            "100d10000+100000",
            [],
            100000,
            1_100_000,
            true), result.WeaponResult);
        Assert.Equal(new DamageComponentResult("4d6", [], 0, 24, true), result.DamageBonusResult);
        Assert.Equal(1_100_024, result.GrossDamage);
    }

    [Theory]
    [InlineData(42, 43, 0, "0")]
    [InlineData(0, 0, -2, "-2")]
    public void InitiatorExtreme_PreservesFlatZeroOrNegativeBonus(
        int str,
        int siz,
        int expectedBonus,
        string expectedExpression)
    {
        var result = Resolve(
            expression: "2d4-1",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(str, siz),
            mode: CombatDamageMode.InitiatorExtremeEligible);

        Assert.Equal(new DamageComponentResult(expectedExpression, [], expectedBonus, expectedBonus, true),
            result.DamageBonusResult);
        Assert.Equal(Math.Max(0, 7 + expectedBonus), result.GrossDamage);
    }

    [Fact]
    public void InitiatorExtreme_WhenWeaponExcludesBonus_UsesZeroWithoutAnyRolls()
    {
        var result = Resolve(
            expression: "1d6+2",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(182, 183),
            mode: CombatDamageMode.InitiatorExtremeEligible,
            addsDamageBonus: false);

        Assert.Equal(new DamageComponentResult("0", [], 0, 0, true), result.DamageBonusResult);
        Assert.Equal(8, result.GrossDamage);
    }

    [Fact]
    public void InitiatorExtreme_RejectsAnyRollInput()
    {
        var weaponRoll = new GenericDiceRoll(1, 6, [3], 3);
        var bonusRoll = new GenericDiceRoll(1, 4, [2], 2);

        Assert.Throws<ArgumentException>(() => Resolve(
            "1d6",
            CocCombatDamageRules.DeriveDamageBonus(65, 60),
            CombatDamageMode.InitiatorExtremeEligible,
            weaponRoll: weaponRoll));
        Assert.Throws<ArgumentException>(() => Resolve(
            "1d6",
            CocCombatDamageRules.DeriveDamageBonus(65, 60),
            CombatDamageMode.InitiatorExtremeEligible,
            damageBonusRoll: bonusRoll));
    }

    [Fact]
    public void FightBackRegularCap_UsesActualRollsInsteadOfMaxima()
    {
        var result = Resolve(
            expression: "1d6+1",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(65, 60),
            mode: CombatDamageMode.FightBackRegularCap,
            weaponRoll: new GenericDiceRoll(1, 6, [2], 2),
            damageBonusRoll: new GenericDiceRoll(1, 4, [1], 1));

        Assert.Equal(3, result.WeaponResult.Total);
        Assert.Equal(1, result.DamageBonusResult.Total);
        Assert.False(result.WeaponResult.Maximized);
        Assert.False(result.DamageBonusResult.Maximized);
        Assert.Equal(4, result.GrossDamage);
    }

    [Theory]
    [InlineData(2, 5)]
    [InlineData(7, 0)]
    [InlineData(9, 0)]
    public void Armor_IsAppliedAfterGrossDamageAndClampedAtZero(int armor, int expectedNet)
    {
        var result = Resolve(
            expression: "1d6",
            damageBonus: CocCombatDamageRules.DeriveDamageBonus(65, 60),
            mode: CombatDamageMode.Regular,
            weaponRoll: new GenericDiceRoll(1, 6, [4], 4),
            damageBonusRoll: new GenericDiceRoll(1, 4, [3], 3),
            armor: armor);

        Assert.Equal(7, result.GrossDamage);
        Assert.Equal(armor, result.Armor);
        Assert.Equal(expectedNet, result.NetDamage);
    }

    [Fact]
    public void RegularDamage_RequiresTheWeaponRoll()
    {
        Assert.Throws<ArgumentException>(() => Resolve(
            "1d6",
            CocCombatDamageRules.DeriveDamageBonus(42, 43),
            CombatDamageMode.Regular));
    }

    [Fact]
    public void RegularDamage_RequiresDiceBonusRollWhenEnabled()
    {
        Assert.Throws<ArgumentException>(() => Resolve(
            "1d6",
            CocCombatDamageRules.DeriveDamageBonus(65, 60),
            CombatDamageMode.Regular,
            weaponRoll: new GenericDiceRoll(1, 6, [3], 3)));
    }

    [Theory]
    [MemberData(nameof(InvalidRequiredRolls))]
    public void RegularDamage_RejectsInvalidRequiredRollShape(
        GenericDiceRoll weaponRoll,
        GenericDiceRoll damageBonusRoll)
    {
        Assert.Throws<ArgumentException>(() => Resolve(
            "2d6",
            CocCombatDamageRules.DeriveDamageBonus(65, 60),
            CombatDamageMode.Regular,
            weaponRoll,
            damageBonusRoll));
    }

    [Fact]
    public void Resolve_RejectsUndefinedDamageModeBeforeUsingRolls()
    {
        Assert.Throws<NotSupportedException>(() => Resolve(
            "1d6",
            CocCombatDamageRules.DeriveDamageBonus(42, 43),
            (CombatDamageMode)999));
    }

    [Fact]
    public void Resolve_RejectsUnsupportedWeaponModeBeforeUsingRolls()
    {
        var weapon = CocCombatDamageRules.NormalizeWeapon(
            "club",
            "Club",
            "1d6",
            addsDamageBonus: true,
            "melee_non_impaling") with
        {
            Mode = "impaling"
        };
        var input = new CombatDamageInput(
            weapon,
            CocCombatDamageRules.DeriveDamageBonus(42, 43),
            CombatDamageMode.Regular,
            WeaponRoll: null,
            DamageBonusRoll: null,
            Armor: 0);

        Assert.Throws<NotSupportedException>(() => new CocCombatDamageEngine().Resolve(input));
    }

    [Fact]
    public void CombatDamageFixture_RequiresExactly48UniqueLabeledCasesAndReferenceMetadata()
    {
        var path = GetCombatDamageFixturePath();
        Assert.True(
            File.Exists(path),
            "Task 4 must generate the combat-damage fixture from the actual Single Player JavaScript before this conformance test can run.");

        var fixture = JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), JsonOptions);

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.Version);
        Assert.Equal(RequiredReferenceSources, fixture.ReferenceSources);
        Assert.Equal(48, fixture.Cases.Count);
        Assert.Equal(48, fixture.Cases.Select(testCase => testCase.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(RequiredFixtureCaseIds, fixture.Cases.Select(testCase => testCase.Id));
        Assert.All(fixture.Cases, testCase => Assert.Contains(testCase.Scope, AllowedScopes));

        var referenceIssue = Assert.Single(
            fixture.Cases,
            testCase => testCase.Id == "target-already-defeated-reference-issue");
        Assert.Equal("potential_reference_issue", referenceIssue.Scope);
        Assert.False(referenceIssue.Expected.GetProperty("applied").GetBoolean());
        Assert.Equal("target_already_defeated", referenceIssue.Expected.GetProperty("reason").GetString());
        Assert.Equal(
            referenceIssue.Expected.GetProperty("revisionBefore").GetInt32(),
            referenceIssue.Expected.GetProperty("revisionAfter").GetInt32());
        Assert.True(referenceIssue.Expected.GetProperty("disposition").GetProperty("pending").GetBoolean());
        Assert.False(referenceIssue.Expected.GetProperty("disposition").GetProperty("hpCommitted").GetBoolean());
    }

    [Fact]
    public void CombatDamageFixture_ConformsToApplicableActualJavaScriptCases()
    {
        var fixture = JsonSerializer.Deserialize<FixtureDocument>(
            File.ReadAllText(GetCombatDamageFixturePath()),
            JsonOptions);

        Assert.NotNull(fixture);
        foreach (var testCase in fixture.Cases.Where(testCase => testCase.Scope == "reference_conformance"))
        {
            AssertReferenceConformance(testCase);
        }
    }

    private sealed record FixtureDocument(
        int Version,
        IReadOnlyList<string> ReferenceSources,
        IReadOnlyList<FixtureCase> Cases);

    private sealed record FixtureCase(
        string Id,
        string Scope,
        JsonElement Input,
        JsonElement Expected);

    private static string GetCombatDamageFixturePath()
    {
        var copiedFixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "combat-damage.json");
        if (File.Exists(copiedFixture))
        {
            return copiedFixture;
        }

        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Fixtures",
            "combat-damage.json"));
    }

    private static void AssertReferenceConformance(FixtureCase testCase)
    {
        if (testCase.Id.StartsWith("db-", StringComparison.Ordinal))
        {
            AssertDamageBonusFixtureCase(testCase);
            return;
        }

        if (testCase.Id.StartsWith("dice-", StringComparison.Ordinal))
        {
            AssertDiceFixtureCase(testCase);
            return;
        }

        if (testCase.Id.StartsWith("weapon-", StringComparison.Ordinal))
        {
            AssertWeaponFixtureCase(testCase);
            return;
        }

        AssertDamageAmountFixtureCase(testCase);
    }

    private static void AssertDamageBonusFixtureCase(FixtureCase testCase)
    {
        var expected = testCase.Expected;
        var actual = CocCombatDamageRules.DeriveDamageBonus(
            testCase.Input.GetProperty("str").GetInt32(),
            testCase.Input.GetProperty("siz").GetInt32());

        Assert.Equal(expected.GetProperty("sum").GetInt32(), actual.Sum);
        Assert.Equal(expected.GetProperty("kind").GetString(), ToFixtureKind(actual.Kind));
        Assert.Equal(expected.GetProperty("expression").GetString(), actual.Expression);
        Assert.Equal(expected.GetProperty("max").GetInt32(), actual.Maximum);
        if (actual.Kind == DamageBonusKind.Flat)
        {
            Assert.Equal(expected.GetProperty("value").GetInt32(), actual.FlatValue);
        }
        else
        {
            Assert.Equal(expected.GetProperty("count").GetInt32(), actual.Count);
            Assert.Equal(expected.GetProperty("faces").GetInt32(), actual.Faces);
        }
    }

    private static void AssertDiceFixtureCase(FixtureCase testCase)
    {
        var expression = testCase.Input.GetProperty("expression").GetString()!;
        var expected = testCase.Expected;
        if (!expected.GetProperty("ok").GetBoolean())
        {
            Assert.Throws<DiceExpressionRuleException>(() => DiceExpressionParser.Parse(expression));
            return;
        }

        var expectedValue = expected.GetProperty("value");
        var actual = DiceExpressionParser.Parse(expression);
        Assert.Equal(expectedValue.GetProperty("text").GetString(), actual.Text);
        Assert.Equal(expectedValue.GetProperty("count").GetInt32(), actual.Count);
        Assert.Equal(expectedValue.GetProperty("faces").GetInt32(), actual.Faces);
        Assert.Equal(expectedValue.GetProperty("modifier").GetInt32(), actual.Modifier);
    }

    private static void AssertWeaponFixtureCase(FixtureCase testCase)
    {
        var input = testCase.Input.GetProperty("weapon");
        var expected = testCase.Expected;
        if (!expected.GetProperty("ok").GetBoolean())
        {
            Assert.Throws<NotSupportedException>(() => NormalizeFixtureWeapon(input));
            return;
        }

        var expectedValue = expected.GetProperty("value");
        var actual = NormalizeFixtureWeapon(input);
        Assert.Equal(expectedValue.GetProperty("id").GetString(), actual.WeaponId);
        Assert.Equal(expectedValue.GetProperty("label").GetString(), actual.Label);
        Assert.Equal(expectedValue.GetProperty("damage").GetString(), actual.Damage.Text);
        Assert.Equal(expectedValue.GetProperty("addsDamageBonus").GetBoolean(), actual.AddsDamageBonus);
        Assert.Equal(expectedValue.GetProperty("mode").GetString(), actual.Mode);
    }

    private static void AssertDamageAmountFixtureCase(FixtureCase testCase)
    {
        var input = testCase.Input;
        var expected = testCase.Id.StartsWith("armor-", StringComparison.Ordinal)
            ? testCase.Expected.GetProperty("result")
            : testCase.Expected;
        var damageBonus = CocCombatDamageRules.DeriveDamageBonus(
            input.GetProperty("str").GetInt32(),
            input.GetProperty("siz").GetInt32());
        var weapon = NormalizeFixtureWeapon(input.GetProperty("weapon"));
        var mode = ToCombatDamageMode(input.GetProperty("mode").GetString());
        var weaponRoll = CreateFixtureRoll(weapon.Damage, input.GetProperty("weaponRolls"));
        var bonusRoll = damageBonus.Kind == DamageBonusKind.Dice
            ? CreateFixtureRoll(
                new DiceExpression(damageBonus.Expression, damageBonus.Count, damageBonus.Faces, 0),
                input.GetProperty("bonusRolls"))
            : null;
        if (mode == CombatDamageMode.InitiatorExtremeEligible)
        {
            weaponRoll = null;
            bonusRoll = null;
        }

        var actual = new CocCombatDamageEngine().Resolve(new CombatDamageInput(
            weapon,
            damageBonus,
            mode,
            weaponRoll,
            bonusRoll,
            input.GetProperty("armor").GetInt32()));

        Assert.Equal(expected.GetProperty("grossDamage").GetInt32(), actual.GrossDamage);
        if (expected.TryGetProperty("netDamage", out var expectedNetDamage))
        {
            Assert.Equal(expectedNetDamage.GetInt32(), actual.NetDamage);
        }
        AssertDamageComponent(expected.GetProperty("weaponResult"), actual.WeaponResult);
        AssertDamageComponent(expected.GetProperty("damageBonusResult"), actual.DamageBonusResult);
    }

    private static CombatWeaponProfile NormalizeFixtureWeapon(JsonElement input)
    {
        return CocCombatDamageRules.NormalizeWeapon(
            input.GetProperty("id").GetString()!,
            input.GetProperty("label").GetString()!,
            input.GetProperty("damage").GetString()!,
            input.GetProperty("addsDamageBonus").GetBoolean(),
            input.GetProperty("mode").GetString()!);
    }

    private static GenericDiceRoll? CreateFixtureRoll(DiceExpression expression, JsonElement rollsElement)
    {
        var rolls = rollsElement.EnumerateArray().Select(value => value.GetInt32()).ToArray();
        if (rolls.Length == 0)
        {
            return null;
        }

        return new GenericDiceRoll(expression.Count, expression.Faces, rolls, rolls.Sum());
    }

    private static void AssertDamageComponent(JsonElement expected, DamageComponentResult actual)
    {
        Assert.Equal(expected.GetProperty("expression").GetString(), actual.Expression);
        Assert.Equal(
            expected.GetProperty("rawRolls").EnumerateArray().Select(value => value.GetInt32()),
            actual.RawRolls);
        Assert.Equal(expected.GetProperty("total").GetInt32(), actual.Total);
        Assert.Equal(
            expected.TryGetProperty("maximized", out var expectedMaximized) && expectedMaximized.GetBoolean(),
            actual.Maximized);
    }

    private static string ToFixtureKind(DamageBonusKind kind)
    {
        return kind switch
        {
            DamageBonusKind.Flat => "flat",
            DamageBonusKind.Dice => "dice",
            _ => throw new NotSupportedException($"Unsupported damage bonus kind: {kind}")
        };
    }

    private static CombatDamageMode ToCombatDamageMode(string? mode)
    {
        return mode switch
        {
            "regular" => CombatDamageMode.Regular,
            "initiator_extreme_eligible" => CombatDamageMode.InitiatorExtremeEligible,
            "fight_back_regular_cap" => CombatDamageMode.FightBackRegularCap,
            _ => throw new NotSupportedException($"Unsupported fixture damage mode: {mode}")
        };
    }

    public static TheoryData<GenericDiceRoll, GenericDiceRoll> InvalidRequiredRolls => new()
    {
        { new GenericDiceRoll(1, 6, [3, 3], 6), new GenericDiceRoll(1, 4, [2], 2) },
        { new GenericDiceRoll(2, 8, [3, 3], 6), new GenericDiceRoll(1, 4, [2], 2) },
        { new GenericDiceRoll(2, 6, [3], 3), new GenericDiceRoll(1, 4, [2], 2) },
        { new GenericDiceRoll(2, 6, [0, 3], 3), new GenericDiceRoll(1, 4, [2], 2) },
        { new GenericDiceRoll(2, 6, [3, 7], 10), new GenericDiceRoll(1, 4, [2], 2) },
        { new GenericDiceRoll(2, 6, [3, 3], 5), new GenericDiceRoll(1, 4, [2], 2) },
        { new GenericDiceRoll(2, 6, [3, 3], 6), new GenericDiceRoll(2, 4, [2], 2) },
        { new GenericDiceRoll(2, 6, [3, 3], 6), new GenericDiceRoll(1, 6, [2], 2) },
        { new GenericDiceRoll(2, 6, [3, 3], 6), new GenericDiceRoll(1, 4, [], 0) },
        { new GenericDiceRoll(2, 6, [3, 3], 6), new GenericDiceRoll(1, 4, [5], 5) },
        { new GenericDiceRoll(2, 6, [3, 3], 6), new GenericDiceRoll(1, 4, [2], 3) }
    };

    private static CombatDamageCalculation Resolve(
        string expression,
        DamageBonusProfile damageBonus,
        CombatDamageMode mode,
        GenericDiceRoll? weaponRoll = null,
        GenericDiceRoll? damageBonusRoll = null,
        bool addsDamageBonus = true,
        int armor = 0)
    {
        var weapon = CocCombatDamageRules.NormalizeWeapon(
            "test-weapon",
            "Test Weapon",
            expression,
            addsDamageBonus,
            "melee_non_impaling");

        return new CocCombatDamageEngine().Resolve(new CombatDamageInput(
            weapon,
            damageBonus,
            mode,
            weaponRoll,
            damageBonusRoll,
            armor));
    }

    private static void AssertComponent(
        DamageComponentResult actual,
        string expectedExpression,
        IReadOnlyList<int> expectedRawRolls,
        int expectedModifier,
        int expectedTotal,
        bool maximized)
    {
        Assert.Equal(expectedExpression, actual.Expression);
        Assert.Equal(expectedRawRolls, actual.RawRolls);
        Assert.Equal(expectedModifier, actual.Modifier);
        Assert.Equal(expectedTotal, actual.Total);
        Assert.Equal(maximized, actual.Maximized);
    }
}
