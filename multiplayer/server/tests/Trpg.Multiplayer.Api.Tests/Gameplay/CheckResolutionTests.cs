using System.Text.Json;
using Trpg.Multiplayer.Api.Gameplay;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class CheckResolutionTests
{
    [Fact]
    public void CocEngine_ConsumesEveryCommittedSinglePlayerFixtureCase()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "check-resolution.json");
        var fixture = JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.Version);
        Assert.Equal("src/check-engine.js", fixture.ReferenceSource);
        Assert.NotEmpty(fixture.Cases);
        var engine = new CocCheckResolutionEngine();

        foreach (var testCase in fixture.Cases)
        {
            var dice = BuildFixtureDiceRoll(testCase.Input);
            Assert.Equal(testCase.Expected.RawRolls, dice.RawRolls);
            Assert.Equal(testCase.Expected.Roll, dice.SelectedRoll);
            var actual = engine.Resolve(new CheckResolutionInput(
                testCase.Input.Target,
                testCase.Input.Difficulty,
                testCase.Input.BonusDice,
                testCase.Input.PenaltyDice,
                dice.SelectedRoll));

            Assert.Equal(testCase.Expected.Roll, actual.Roll);
            Assert.Equal(testCase.Expected.Target, actual.Target);
            Assert.Equal(testCase.Expected.Difficulty, actual.Difficulty);
            Assert.Equal(testCase.Expected.DifficultyTarget, actual.DifficultyTarget);
            Assert.Equal(testCase.Expected.SuccessLevel, actual.SuccessLevel);
            Assert.Equal(testCase.Expected.Passed, actual.Passed);
            Assert.Equal(testCase.Expected.Critical, actual.Critical);
            Assert.Equal(testCase.Expected.Fumble, actual.Fumble);
        }
    }

    [Fact]
    public void SecureDiceRoller_ReturnsOnlyValidPercentileValuesAndHonorsNetDiceCount()
    {
        var roller = new SecureDiceRoller();

        foreach (var dice in new[] { (0, 0), (2, 0), (0, 2), (2, 1), (1, 2) })
        {
            var result = roller.RollPercentile(dice.Item1, dice.Item2);
            Assert.Equal(1 + Math.Max(dice.Item1 - dice.Item2, dice.Item2 - dice.Item1), result.RawRolls.Count);
            Assert.All(result.RawRolls, roll => Assert.InRange(roll, 1, 100));
            Assert.InRange(result.SelectedRoll, 1, 100);
        }
    }

    [Theory]
    [InlineData("d3", "d3", 1, 3, 0)]
    [InlineData("1d3", "1d3", 1, 3, 0)]
    [InlineData("1d6+2", "1d6+2", 1, 6, 2)]
    [InlineData("2d4-1", "2d4-1", 2, 4, -1)]
    [InlineData("  D6+2 ", "d6+2", 1, 6, 2)]
    [InlineData("1d2-100000", "1d2-100000", 1, 2, -100000)]
    [InlineData("1d10000+100000", "1d10000+100000", 1, 10000, 100000)]
    public void DiceExpressionParser_AcceptsExactGrammarAndNormalizesOnlyCaseAndOuterWhitespace(
        string input,
        string expectedText,
        int expectedCount,
        int expectedFaces,
        int expectedModifier)
    {
        var expression = DiceExpressionParser.Parse(input);

        Assert.Equal(new DiceExpression(expectedText, expectedCount, expectedFaces, expectedModifier), expression);
    }

    [Theory]
    [InlineData("", DiceExpressionError.InvalidLength)]
    [InlineData("                                 ", DiceExpressionError.InvalidLength)]
    [InlineData("1d2+12345678901234567890123456789", DiceExpressionError.InvalidLength)]
    [InlineData("1D6 + 2", DiceExpressionError.InvalidFormat)]
    [InlineData("1d٦", DiceExpressionError.InvalidFormat)]
    [InlineData("d", DiceExpressionError.InvalidFormat)]
    [InlineData("0d6", DiceExpressionError.InvalidCount)]
    [InlineData("101d6", DiceExpressionError.InvalidCount)]
    [InlineData("1d1", DiceExpressionError.InvalidFaces)]
    [InlineData("1d10001", DiceExpressionError.InvalidFaces)]
    [InlineData("1d2-100001", DiceExpressionError.InvalidModifier)]
    [InlineData("1d2+100001", DiceExpressionError.InvalidModifier)]
    public void DiceExpressionParser_RejectsInvalidSyntaxAndBounds(
        string input,
        DiceExpressionError expectedError)
    {
        var exception = Assert.Throws<DiceExpressionRuleException>(() => DiceExpressionParser.Parse(input));

        Assert.Equal(expectedError, exception.Error);
    }

    [Fact]
    public void SecureDiceRoller_ReturnsValidatedGenericDiceAndCheckedTotal()
    {
        var result = new SecureDiceRoller().RollDice(new DiceRollRequest(100, 10000));

        Assert.Equal(100, result.Count);
        Assert.Equal(10000, result.Faces);
        Assert.Equal(100, result.RawRolls.Count);
        Assert.All(result.RawRolls, roll => Assert.InRange(roll, 1, 10000));
        Assert.Equal(result.RawRolls.Sum(), result.Total);
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(101, 6)]
    [InlineData(1, 1)]
    [InlineData(1, 10001)]
    public void SecureDiceRoller_RejectsInvalidGenericDiceBeforeRolling(int count, int faces)
    {
        var roller = new SecureDiceRoller();

        Assert.Throws<ArgumentOutOfRangeException>(() => roller.RollDice(new DiceRollRequest(count, faces)));
    }

    [Fact]
    public void CocEngine_RejectsOutOfRangeForcedRolls()
    {
        var engine = new CocCheckResolutionEngine();
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Resolve(new CheckResolutionInput(60, "regular", 0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Resolve(new CheckResolutionInput(60, "regular", 0, 0, 101)));
    }

    [Theory]
    [InlineData("fumble", 0)]
    [InlineData("failure", 0)]
    [InlineData("regular", 1)]
    [InlineData("hard", 2)]
    [InlineData("extreme", 3)]
    [InlineData("critical", 4)]
    public void SuccessLevelRank_matches_single_player_order(string level, int expected)
    {
        Assert.Equal(expected, CocCheckResolutionRules.SuccessLevelRank(level));
    }

    private sealed record FixtureDocument(int Version, string ReferenceSource, IReadOnlyList<FixtureCase> Cases);

    private sealed record FixtureCase(string Name, FixtureInput Input, FixtureExpected Expected);

    private sealed record FixtureInput(int Target, string Difficulty, int BonusDice, int PenaltyDice, IReadOnlyList<int> RandomSequence);

    private sealed record FixtureExpected(
        IReadOnlyList<int> RawRolls,
        int Roll,
        int Target,
        string Difficulty,
        int DifficultyTarget,
        string SuccessLevel,
        bool Passed,
        bool Critical,
        bool Fumble);

    private static PercentileDiceRoll BuildFixtureDiceRoll(FixtureInput input)
    {
        var net = input.BonusDice - input.PenaltyDice;
        var bonus = Math.Max(0, net);
        var penalty = Math.Max(0, -net);
        var expectedCount = 1 + Math.Max(bonus, penalty);
        Assert.Equal(expectedCount + 1, input.RandomSequence.Count);
        var ones = input.RandomSequence[0];
        var rawRolls = input.RandomSequence
            .Skip(1)
            .Select(tens => tens == 0 && ones == 0 ? 100 : tens * 10 + ones)
            .ToArray();
        var selected = bonus > 0 ? rawRolls.Min() : penalty > 0 ? rawRolls.Max() : rawRolls[0];
        return new PercentileDiceRoll(selected, rawRolls);
    }
}
