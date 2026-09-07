using System.Globalization;
using System.Text.RegularExpressions;

namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record DiceExpression(string Text, int Count, int Faces, int Modifier);

public enum DiceExpressionError
{
    InvalidLength,
    InvalidFormat,
    InvalidCount,
    InvalidFaces,
    InvalidModifier
}

public sealed class DiceExpressionRuleException : ArgumentException
{
    public DiceExpressionRuleException(DiceExpressionError error)
        : base(GetMessage(error))
    {
        Error = error;
    }

    public DiceExpressionError Error { get; }

    private static string GetMessage(DiceExpressionError error) => error switch
    {
        DiceExpressionError.InvalidLength => "Dice expression length must be between 1 and 32 characters.",
        DiceExpressionError.InvalidFormat => "Dice expression must use NdM, dM, NdM+K, or NdM-K format.",
        DiceExpressionError.InvalidCount => "Dice count must be between 1 and 100.",
        DiceExpressionError.InvalidFaces => "Dice faces must be between 2 and 10000.",
        DiceExpressionError.InvalidModifier => "Dice modifier must be between -100000 and 100000.",
        _ => "Invalid dice expression."
    };
}

public static partial class DiceExpressionParser
{
    private const int MaximumExpressionLength = 32;
    private const int MaximumDiceCount = 100;
    private const int MaximumFaces = 10000;
    private const int MaximumModifier = 100000;

    public static DiceExpression Parse(string input)
    {
        var text = input?.Trim().ToLowerInvariant() ?? string.Empty;
        if (text.Length is 0 or > MaximumExpressionLength)
        {
            throw new DiceExpressionRuleException(DiceExpressionError.InvalidLength);
        }

        var match = DiceExpressionRegex().Match(text);
        if (!match.Success)
        {
            throw new DiceExpressionRuleException(DiceExpressionError.InvalidFormat);
        }

        var countText = match.Groups[1].Value;
        var count = countText.Length == 0
            ? 1
            : ParseBoundedInteger(countText, 1, MaximumDiceCount, DiceExpressionError.InvalidCount);
        var faces = ParseBoundedInteger(
            match.Groups[2].Value,
            2,
            MaximumFaces,
            DiceExpressionError.InvalidFaces);
        var modifierText = match.Groups[3].Value;
        var modifier = modifierText.Length == 0
            ? 0
            : ParseBoundedInteger(
                modifierText,
                -MaximumModifier,
                MaximumModifier,
                DiceExpressionError.InvalidModifier,
                NumberStyles.AllowLeadingSign);

        return new DiceExpression(text, count, faces, modifier);
    }

    private static int ParseBoundedInteger(
        string value,
        int minimum,
        int maximum,
        DiceExpressionError error,
        NumberStyles numberStyles = NumberStyles.None)
    {
        if (!int.TryParse(value, numberStyles, CultureInfo.InvariantCulture, out var parsed) ||
            parsed < minimum ||
            parsed > maximum)
        {
            throw new DiceExpressionRuleException(error);
        }

        return parsed;
    }

    [GeneratedRegex(
        "^(\\d*)d(\\d+)([+-]\\d+)?$",
        RegexOptions.CultureInvariant | RegexOptions.ECMAScript)]
    private static partial Regex DiceExpressionRegex();
}
