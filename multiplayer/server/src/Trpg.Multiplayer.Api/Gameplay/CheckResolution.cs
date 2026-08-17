using System.Security.Cryptography;

namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record CheckResolutionInput(
    int Target,
    string Difficulty,
    int BonusDice,
    int PenaltyDice,
    int Roll);

public sealed record CheckResolutionResult(
    int Roll,
    int Target,
    string Difficulty,
    int DifficultyTarget,
    string SuccessLevel,
    bool Passed,
    bool Critical,
    bool Fumble);

public sealed record PercentileDiceRoll(int SelectedRoll, IReadOnlyList<int> RawRolls);

public interface IDiceRoller
{
    PercentileDiceRoll RollPercentile(int bonusDice, int penaltyDice);
}

public interface ICheckResolutionEngine
{
    CheckResolutionResult Resolve(CheckResolutionInput input);
}

public sealed class SecureDiceRoller : IDiceRoller
{
    public PercentileDiceRoll RollPercentile(int bonusDice, int penaltyDice)
    {
        ValidateDiceCount(bonusDice);
        ValidateDiceCount(penaltyDice);
        var net = bonusDice - penaltyDice;
        var bonus = Math.Max(0, net);
        var penalty = Math.Max(0, -net);
        var extra = Math.Max(bonus, penalty);
        var ones = RandomNumberGenerator.GetInt32(0, 10);
        var values = Enumerable.Range(0, 1 + extra)
            .Select(_ => ToPercentile(RandomNumberGenerator.GetInt32(0, 10), ones))
            .ToArray();
        var selected = bonus > 0 ? values.Min() : penalty > 0 ? values.Max() : values[0];
        return new PercentileDiceRoll(selected, values);
    }

    private static int ToPercentile(int tens, int ones) => tens == 0 && ones == 0 ? 100 : tens * 10 + ones;

    private static void ValidateDiceCount(int value)
    {
        if (value is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}

public sealed class CocCheckResolutionEngine : ICheckResolutionEngine
{
    private static readonly IReadOnlyDictionary<string, int> SuccessOrder = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["fumble"] = 0,
        ["failure"] = 0,
        ["regular"] = 1,
        ["hard"] = 2,
        ["extreme"] = 3,
        ["critical"] = 4
    };

    public CheckResolutionResult Resolve(CheckResolutionInput input)
    {
        if (input.Target is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(input.Target));
        }

        if (input.Roll is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(input.Roll));
        }

        if (!CheckDifficulty.IsSupported(input.Difficulty))
        {
            throw new ArgumentException("Unsupported check difficulty.", nameof(input.Difficulty));
        }

        var difficultyTarget = DifficultyTarget(input.Target, input.Difficulty);
        var successLevel = Rank(input.Roll, input.Target);
        var required = input.Difficulty switch
        {
            "hard" => 2,
            "extreme" => 3,
            _ => 1
        };
        var passed = SuccessOrder[successLevel] >= required;
        return new CheckResolutionResult(
            input.Roll,
            input.Target,
            input.Difficulty,
            difficultyTarget,
            successLevel,
            passed,
            successLevel == "critical",
            successLevel == "fumble");
    }

    private static string Rank(int roll, int target)
    {
        if (roll == 1)
        {
            return "critical";
        }

        if (target < 50 ? roll >= 96 : roll == 100)
        {
            return "fumble";
        }

        if (roll <= Math.Floor(target / 5m))
        {
            return "extreme";
        }

        if (roll <= Math.Floor(target / 2m))
        {
            return "hard";
        }

        return roll <= target ? "regular" : "failure";
    }

    private static int DifficultyTarget(int target, string difficulty) => difficulty switch
    {
        "hard" => (int)Math.Floor(target / 2m),
        "extreme" => (int)Math.Floor(target / 5m),
        _ => target
    };
}

public static class CheckDifficulty
{
    public static bool IsSupported(string? difficulty) => difficulty is "regular" or "hard" or "extreme";
}
