namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record HpConCheck(int Roll, int Target, bool Success);

public sealed record HpDamageEvent(
    string EventKey,
    int Damage,
    bool MajorWound,
    bool InstantDeath,
    HpConCheck? ConCheck);

public sealed record CharacterHealthState(
    int CurrentHp,
    int MaxHp,
    int Con,
    bool MajorWound,
    bool Unconscious,
    bool Dying,
    bool Dead,
    IReadOnlyList<HpDamageEvent> History,
    HpDamageEvent? LastDamageEvent)
{
    public const int HistoryLimit = 80;
}

public sealed record HpDamageInput(string EventKey, int Damage, int? ConRoll);

public sealed record HpDamageResolutionResult(CharacterHealthState State, HpDamageEvent? Event, bool Changed, bool Deduped);

public interface IHpDamageEngine
{
    HpDamageResolutionResult Apply(CharacterHealthState state, HpDamageInput input);
}

public sealed class CocHpDamageEngine : IHpDamageEngine
{
    public HpDamageResolutionResult Apply(CharacterHealthState state, HpDamageInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ValidateState(state);
        if (string.IsNullOrWhiteSpace(input.EventKey))
        {
            throw new ArgumentException("Damage event key is required.", nameof(input));
        }

        if (input.Damage <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input), "Canonical damage must be positive.");
        }

        if (state.History.Any(item => string.Equals(item.EventKey, input.EventKey, StringComparison.Ordinal)))
        {
            return new HpDamageResolutionResult(state, state.History.Single(item => string.Equals(item.EventKey, input.EventKey, StringComparison.Ordinal)), false, true);
        }

        if (state.Dead)
        {
            return new HpDamageResolutionResult(state, null, false, false);
        }

        var threshold = MajorWoundThreshold(state.MaxHp);
        var instantDeath = input.Damage >= state.MaxHp;
        var majorWound = !instantDeath && input.Damage >= threshold;
        HpConCheck? conCheck = null;
        if (majorWound)
        {
            if (input.ConRoll is null || input.ConRoll.Value is < 1 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(input), "Major wound resolution requires a percentile CON roll.");
            }

            var conRoll = input.ConRoll.Value;
            conCheck = new HpConCheck(conRoll, state.Con, conRoll <= state.Con);
        }

        var currentHp = Math.Max(0, state.CurrentHp - input.Damage);
        var damageEvent = new HpDamageEvent(input.EventKey, input.Damage, majorWound, instantDeath, conCheck);
        var history = state.History.Concat([damageEvent]).TakeLast(CharacterHealthState.HistoryLimit).ToArray();

        var next = instantDeath
            ? state with
            {
                CurrentHp = currentHp,
                Unconscious = false,
                Dying = false,
                Dead = true,
                History = history,
                LastDamageEvent = damageEvent
            }
            : state with
            {
                CurrentHp = currentHp,
                MajorWound = state.MajorWound || majorWound,
                Unconscious = state.Unconscious || (majorWound && conCheck is { Success: false }) || currentHp == 0,
                Dying = state.Dying || (currentHp == 0 && (state.MajorWound || majorWound)),
                History = history,
                LastDamageEvent = damageEvent
            };

        return new HpDamageResolutionResult(next, damageEvent, true, false);
    }

    public static int MajorWoundThreshold(int maxHp) => Math.Max(1, (Math.Max(1, maxHp) + 1) / 2);

    private static void ValidateState(CharacterHealthState state)
    {
        if (state.MaxHp < 1 || state.CurrentHp < 0 || state.CurrentHp > state.MaxHp || state.Con is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Health state is outside the supported HP contract.");
        }
    }
}
