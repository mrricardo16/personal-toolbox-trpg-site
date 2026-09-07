namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record HpConCheck(int Roll, int Target, bool Success);

public sealed record HpDamageEvent(
    string EventKey,
    int Damage,
    bool MajorWound,
    bool InstantDeath,
    HpConCheck? ConCheck);

public sealed record CharacterHealthState
{
    public const int HistoryLimit = 80;
    public const int TreatmentHistoryLimit = 60;
    public const int DyingCheckHistoryLimit = 40;

    public CharacterHealthState(
        int currentHp,
        int maxHp,
        int con,
        bool majorWound,
        bool unconscious,
        DyingEpisodeState? dyingEpisode,
        StabilizedConditionState? stabilized,
        DeadConditionState? deadCondition,
        IReadOnlyList<TreatmentRecord> treatmentHistory,
        IReadOnlyList<HpDamageEvent> history,
        HpDamageEvent? lastDamageEvent)
    {
        CurrentHp = currentHp;
        MaxHp = maxHp;
        Con = con;
        MajorWound = majorWound;
        Unconscious = unconscious;
        DyingEpisode = dyingEpisode;
        Stabilized = stabilized;
        DeadCondition = deadCondition;
        TreatmentHistory = treatmentHistory ?? [];
        History = history ?? [];
        LastDamageEvent = lastDamageEvent;
    }

    // Kept as a source-compatible construction seam for the existing game initialization and HP fixture.
    public CharacterHealthState(
        int CurrentHp,
        int MaxHp,
        int Con,
        bool MajorWound,
        bool Unconscious,
        bool Dying,
        bool Dead,
        IReadOnlyList<HpDamageEvent> History,
        HpDamageEvent? LastDamageEvent)
        : this(
            CurrentHp,
            MaxHp,
            Con,
            MajorWound,
            Unconscious,
            Dying ? new DyingEpisodeState(null, [], 1, false) : null,
            null,
            Dead ? new DeadConditionState(null, "legacy_dead", null) : null,
            [],
            History,
            LastDamageEvent)
    {
    }

    public int CurrentHp { get; init; }

    public int MaxHp { get; init; }

    public int Con { get; init; }

    public bool MajorWound { get; init; }

    public bool Unconscious { get; init; }

    public DyingEpisodeState? DyingEpisode { get; init; }

    public StabilizedConditionState? Stabilized { get; init; }

    public DeadConditionState? DeadCondition { get; init; }

    public IReadOnlyList<TreatmentRecord> TreatmentHistory { get; init; }

    public IReadOnlyList<HpDamageEvent> History { get; init; }

    public HpDamageEvent? LastDamageEvent { get; init; }

    public bool Dying => DyingEpisode is not null;

    public bool Dead => DeadCondition is not null;
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

        var instantDeath = input.Damage >= state.MaxHp;
        var majorWound = RequiresConRoll(state, input.Damage);
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
        var freshDying = !state.Dying && currentHp == 0 && (state.MajorWound || majorWound);

        var next = instantDeath
            ? state with
            {
                CurrentHp = currentHp,
                Unconscious = false,
                DyingEpisode = null,
                Stabilized = null,
                DeadCondition = new DeadConditionState(input.EventKey, "instant_death", null),
                History = history,
                LastDamageEvent = damageEvent
            }
            : state with
            {
                CurrentHp = currentHp,
                MajorWound = state.MajorWound || majorWound,
                Unconscious = state.Unconscious || (majorWound && conCheck is { Success: false }) || currentHp == 0,
                DyingEpisode = freshDying
                    ? new DyingEpisodeState(input.EventKey, [], 1, false)
                    : state.DyingEpisode,
                Stabilized = freshDying ? null : state.Stabilized,
                History = history,
                LastDamageEvent = damageEvent
            };

        return new HpDamageResolutionResult(next, damageEvent, true, false);
    }

    public static int MajorWoundThreshold(int maxHp) => Math.Max(1, (Math.Max(1, maxHp) + 1) / 2);

    // 修改时间：2026-09-07 14:56:39
    // 修改说明：公开 HP 引擎唯一的重伤 CON 判定边界，供战斗伤害与 Apply 共用。
    // 修改原因：避免战斗流程复制重伤阈值，并确保零伤害与即死伤害不会额外掷 CON。
    // 业务影响：仅正数、非即死且达到重伤阈值的伤害需要 CON 百分骰。
    public static bool RequiresConRoll(CharacterHealthState state, int damage)
    {
        ArgumentNullException.ThrowIfNull(state);
        ValidateState(state);
        return damage > 0
            && damage < state.MaxHp
            && damage >= MajorWoundThreshold(state.MaxHp);
    }

    private static void ValidateState(CharacterHealthState state)
    {
        if (state.MaxHp < 1 || state.CurrentHp < 0 || state.CurrentHp > state.MaxHp || state.Con is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Health state is outside the supported HP contract.");
        }
    }
}
