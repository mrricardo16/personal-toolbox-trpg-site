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

    [Fact]
    public void CocEngine_RejectsOutOfRangeForcedRolls()
    {
        var engine = new CocCheckResolutionEngine();
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Resolve(new CheckResolutionInput(60, "regular", 0, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.Resolve(new CheckResolutionInput(60, "regular", 0, 0, 101)));
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
