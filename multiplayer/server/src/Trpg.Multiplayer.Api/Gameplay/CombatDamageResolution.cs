namespace Trpg.Multiplayer.Api.Gameplay;

public enum DamageBonusKind
{
    Flat,
    Dice
}

public enum CombatDamageMode
{
    Regular,
    InitiatorExtremeEligible,
    FightBackRegularCap
}

public sealed record DamageBonusProfile(
    int Sum,
    DamageBonusKind Kind,
    int FlatValue,
    int Count,
    int Faces,
    string Expression,
    int Maximum);

public sealed record CombatWeaponProfile(
    string WeaponId,
    string Label,
    DiceExpression Damage,
    bool AddsDamageBonus,
    string Mode);

public sealed record CombatDamageInput(
    CombatWeaponProfile Weapon,
    DamageBonusProfile DamageBonus,
    CombatDamageMode Mode,
    GenericDiceRoll? WeaponRoll,
    GenericDiceRoll? DamageBonusRoll,
    int Armor);

public sealed record DamageComponentResult(
    string Expression,
    IReadOnlyList<int> RawRolls,
    int Modifier,
    int Total,
    bool Maximized);

public sealed record CombatDamageCalculation(
    DamageComponentResult WeaponResult,
    DamageComponentResult DamageBonusResult,
    int GrossDamage,
    int Armor,
    int NetDamage);

public interface ICombatDamageEngine
{
    CombatDamageCalculation Resolve(CombatDamageInput input);
}

public sealed class CocCombatDamageEngine : ICombatDamageEngine
{
    private const string SupportedWeaponMode = "melee_non_impaling";

    public CombatDamageCalculation Resolve(CombatDamageInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Weapon);
        ArgumentNullException.ThrowIfNull(input.DamageBonus);

        if (!string.Equals(input.Weapon.Mode, SupportedWeaponMode, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Only melee_non_impaling weapon damage is supported.");
        }

        if (!Enum.IsDefined(input.Mode))
        {
            throw new NotSupportedException("Unsupported combat damage mode.");
        }

        var (weaponResult, damageBonusResult) = input.Mode switch
        {
            CombatDamageMode.InitiatorExtremeEligible => ResolveExtreme(input),
            CombatDamageMode.Regular or CombatDamageMode.FightBackRegularCap => ResolveRolled(input),
            _ => throw new NotSupportedException("Unsupported combat damage mode.")
        };

        var grossDamage = Math.Max(0, checked(weaponResult.Total + damageBonusResult.Total));
        var netDamage = Math.Max(0, checked(grossDamage - input.Armor));
        return new CombatDamageCalculation(
            weaponResult,
            damageBonusResult,
            grossDamage,
            input.Armor,
            netDamage);
    }

    private static (DamageComponentResult Weapon, DamageComponentResult DamageBonus) ResolveRolled(
        CombatDamageInput input)
    {
        var damage = input.Weapon.Damage;
        var weaponRoll = ValidateRequiredRoll(input.WeaponRoll, damage.Count, damage.Faces, nameof(input.WeaponRoll));
        var weaponResult = new DamageComponentResult(
            damage.Text,
            weaponRoll.RawRolls,
            damage.Modifier,
            checked(weaponRoll.Total + damage.Modifier),
            false);

        return (weaponResult, ResolveRolledDamageBonus(input));
    }

    private static DamageComponentResult ResolveRolledDamageBonus(CombatDamageInput input)
    {
        if (!input.Weapon.AddsDamageBonus)
        {
            return ZeroDamageBonus(maximized: false);
        }

        var profile = input.DamageBonus;
        if (profile.Kind == DamageBonusKind.Flat)
        {
            return new DamageComponentResult(
                profile.Expression,
                [],
                profile.FlatValue,
                profile.FlatValue,
                false);
        }

        if (profile.Kind != DamageBonusKind.Dice)
        {
            throw new NotSupportedException("Unsupported damage bonus kind.");
        }

        var roll = ValidateRequiredRoll(
            input.DamageBonusRoll,
            profile.Count,
            profile.Faces,
            nameof(input.DamageBonusRoll));
        return new DamageComponentResult(profile.Expression, roll.RawRolls, 0, roll.Total, false);
    }

    private static (DamageComponentResult Weapon, DamageComponentResult DamageBonus) ResolveExtreme(
        CombatDamageInput input)
    {
        if (input.WeaponRoll is not null || input.DamageBonusRoll is not null)
        {
            throw new ArgumentException("Extreme damage must not include roll inputs.", nameof(input));
        }

        var damage = input.Weapon.Damage;
        var weaponMaximum = checked(damage.Count * damage.Faces + damage.Modifier);
        var weaponResult = new DamageComponentResult(
            damage.Text,
            [],
            damage.Modifier,
            weaponMaximum,
            true);

        if (!input.Weapon.AddsDamageBonus)
        {
            return (weaponResult, ZeroDamageBonus(maximized: true));
        }

        var profile = input.DamageBonus;
        var damageBonusResult = profile.Kind switch
        {
            DamageBonusKind.Flat => new DamageComponentResult(
                profile.Expression,
                [],
                profile.FlatValue,
                profile.FlatValue,
                true),
            DamageBonusKind.Dice => new DamageComponentResult(
                profile.Expression,
                [],
                0,
                profile.Maximum,
                true),
            _ => throw new NotSupportedException("Unsupported damage bonus kind.")
        };

        return (weaponResult, damageBonusResult);
    }

    private static GenericDiceRoll ValidateRequiredRoll(
        GenericDiceRoll? roll,
        int expectedCount,
        int expectedFaces,
        string parameterName)
    {
        if (roll is null)
        {
            throw new ArgumentException("A required dice roll is missing.", parameterName);
        }

        if (roll.Count != expectedCount || roll.Faces != expectedFaces)
        {
            throw new ArgumentException("Dice roll count or faces do not match the required expression.", parameterName);
        }

        if (roll.RawRolls is null || roll.RawRolls.Count != expectedCount)
        {
            throw new ArgumentException("Dice roll raw-value count does not match the required expression.", parameterName);
        }

        var rawSum = 0;
        foreach (var rawRoll in roll.RawRolls)
        {
            if (rawRoll < 1 || rawRoll > expectedFaces)
            {
                throw new ArgumentException("Dice roll contains an out-of-range raw value.", parameterName);
            }

            rawSum = checked(rawSum + rawRoll);
        }

        if (roll.Total != rawSum)
        {
            throw new ArgumentException("Dice roll total does not match its raw values.", parameterName);
        }

        return roll;
    }

    private static DamageComponentResult ZeroDamageBonus(bool maximized) =>
        new("0", [], 0, 0, maximized);
}

public static class CocCombatDamageRules
{
    private const string SupportedWeaponMode = "melee_non_impaling";

    public static DamageBonusProfile DeriveDamageBonus(int str, int siz)
    {
        var sum = checked(str + siz);
        var effectiveSum = Math.Max(2, sum);

        if (effectiveSum <= 64)
        {
            return FlatDamageBonus(effectiveSum, -2);
        }

        if (effectiveSum <= 84)
        {
            return FlatDamageBonus(effectiveSum, -1);
        }

        if (effectiveSum <= 124)
        {
            return FlatDamageBonus(effectiveSum, 0);
        }

        if (effectiveSum <= 164)
        {
            return DiceDamageBonus(effectiveSum, 1, 4);
        }

        if (effectiveSum <= 204)
        {
            return DiceDamageBonus(effectiveSum, 1, 6);
        }

        var count = checked(2 + ((effectiveSum - 205) / 80));
        return DiceDamageBonus(effectiveSum, count, 6);
    }

    public static CombatWeaponProfile NormalizeWeapon(
        string weaponId,
        string label,
        string expression,
        bool addsDamageBonus,
        string mode)
    {
        if (!string.Equals(mode, SupportedWeaponMode, StringComparison.Ordinal))
        {
            throw new NotSupportedException("Only melee_non_impaling weapon damage is supported.");
        }

        return new CombatWeaponProfile(
            weaponId,
            label,
            DiceExpressionParser.Parse(expression),
            addsDamageBonus,
            mode);
    }

    private static DamageBonusProfile FlatDamageBonus(int sum, int value) =>
        new(sum, DamageBonusKind.Flat, value, 0, 0, value.ToString(System.Globalization.CultureInfo.InvariantCulture), value);

    private static DamageBonusProfile DiceDamageBonus(int sum, int count, int faces) =>
        new(
            sum,
            DamageBonusKind.Dice,
            0,
            count,
            faces,
            $"{count}d{faces}",
            checked(count * faces));
}
