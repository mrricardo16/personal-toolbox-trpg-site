using System.Text.Json;
using Trpg.Multiplayer.Api.Gameplay;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class HpDamageResolutionTests
{
    [Fact]
    public void CocEngine_ConsumesEveryCommittedSinglePlayerFixtureCase()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "hp-damage.json");
        var fixture = JsonSerializer.Deserialize<FixtureDocument>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(fixture);
        Assert.Equal(1, fixture.Version);
        Assert.Equal("src/hp-damage-state.js", fixture.ReferenceSource);
        Assert.NotEmpty(fixture.Cases);
        var engine = new CocHpDamageEngine();

        foreach (var testCase in fixture.Cases)
        {
            var state = testCase.Initial.ToDomain();
            foreach (var command in testCase.Commands)
            {
                state = engine.Apply(state, command.ToDomain()).State;
            }

            Assert.Equal(testCase.Expected.CurrentHp, state.CurrentHp);
            Assert.Equal(testCase.Expected.MajorWound, state.MajorWound);
            Assert.Equal(testCase.Expected.Unconscious, state.Unconscious);
            Assert.Equal(testCase.Expected.Dying, state.Dying);
            Assert.Equal(testCase.Expected.Dead, state.Dead);
            Assert.Equal(testCase.Expected.HistoryCount, state.History.Count);
            Assert.Equal(testCase.Expected.LastEvent?.EventKey, state.LastDamageEvent?.EventKey);
            Assert.Equal(testCase.Expected.LastEvent?.Damage, state.LastDamageEvent?.Damage);
            Assert.Equal(testCase.Expected.LastEvent?.MajorWound, state.LastDamageEvent?.MajorWound);
            Assert.Equal(testCase.Expected.LastEvent?.InstantDeath, state.LastDamageEvent?.InstantDeath);
            Assert.Equal(testCase.Expected.LastEvent?.ConCheck?.Success, state.LastDamageEvent?.ConCheck?.Success);
        }
    }

    private sealed record FixtureDocument(int Version, string ReferenceSource, IReadOnlyList<FixtureCase> Cases);

    private sealed record FixtureCase(string Name, FixtureState Initial, IReadOnlyList<FixtureCommand> Commands, FixtureState Expected);

    private sealed record FixtureState(
        int CurrentHp,
        int MaxHp,
        int Con,
        bool MajorWound,
        bool Unconscious,
        bool Dying,
        bool Dead,
        IReadOnlyList<FixtureEvent> History,
        FixtureEvent? LastEvent)
    {
        public int HistoryCount => History.Count;

        public CharacterHealthState ToDomain() => new(
            CurrentHp,
            MaxHp,
            Con,
            MajorWound,
            Unconscious,
            Dying,
            Dead,
            History.Select(item => item.ToDomain()).ToArray(),
            LastEvent?.ToDomain());
    }

    private sealed record FixtureCommand(string EventKey, int Damage, int? ConRoll)
    {
        public HpDamageInput ToDomain() => new(EventKey, Damage, ConRoll);
    }

    private sealed record FixtureEvent(string EventKey, int Damage, bool MajorWound, bool InstantDeath, FixtureConCheck? ConCheck)
    {
        public HpDamageEvent ToDomain() => new(EventKey, Damage, MajorWound, InstantDeath, ConCheck?.ToDomain());
    }

    private sealed record FixtureConCheck(int Roll, int Target, bool Success)
    {
        public HpConCheck ToDomain() => new(Roll, Target, Success);
    }
}
