namespace Trpg.Multiplayer.Api.Gameplay;

public enum CombatResponse
{
    Dodge,
    FightBack
}

public enum CombatOpposedError
{
    InvalidResponse
}

public sealed class CombatOpposedRuleException : InvalidOperationException
{
    public CombatOpposedRuleException(CombatOpposedError error)
        : base(error.ToString())
    {
        Error = error;
    }

    public CombatOpposedError Error { get; }
}

public sealed record CombatOpposedResolutionInput(
    CheckResolutionResult AttackerCheck,
    CheckResolutionResult DefenderCheck,
    CombatResponse Response);

public sealed record CombatOpposedResolutionResult(
    string Outcome,
    string? WinnerSide,
    string? DamageMode);

public interface ICombatOpposedEngine
{
    CombatOpposedResolutionResult Resolve(CombatOpposedResolutionInput input);
}

public sealed class CocCombatOpposedEngine : ICombatOpposedEngine
{
    public CombatOpposedResolutionResult Resolve(CombatOpposedResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Response switch
        {
            CombatResponse.Dodge => ResolveDodge(input.AttackerCheck, input.DefenderCheck),
            CombatResponse.FightBack => ResolveFightBack(input.AttackerCheck, input.DefenderCheck),
            _ => throw new CombatOpposedRuleException(CombatOpposedError.InvalidResponse)
        };
    }

    private static CombatOpposedResolutionResult ResolveDodge(
        CheckResolutionResult attacker,
        CheckResolutionResult defender)
    {
        if (attacker.Passed && Rank(attacker) > Rank(defender))
        {
            return AttackerHits(attacker);
        }

        return attacker.Passed || defender.Passed
            ? new CombatOpposedResolutionResult("defender_dodges", "defender", null)
            : NoDamage();
    }

    private static CombatOpposedResolutionResult ResolveFightBack(
        CheckResolutionResult attacker,
        CheckResolutionResult defender)
    {
        if (defender.Passed && Rank(defender) > Rank(attacker))
        {
            return new CombatOpposedResolutionResult(
                "defender_fights_back",
                "defender",
                "fight_back_regular_cap");
        }

        return attacker.Passed ? AttackerHits(attacker) : NoDamage();
    }

    private static CombatOpposedResolutionResult AttackerHits(CheckResolutionResult attacker) => new(
        "attacker_hits",
        "attacker",
        Rank(attacker) >= CocCheckResolutionRules.SuccessLevelRank("extreme")
            ? "initiator_extreme_eligible"
            : "regular");

    private static CombatOpposedResolutionResult NoDamage() => new("both_fail_no_damage", null, null);

    private static int Rank(CheckResolutionResult check) =>
        CocCheckResolutionRules.SuccessLevelRank(check.SuccessLevel);
}
