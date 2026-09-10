using Trpg.Multiplayer.Api.Gameplay;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class NpcCombatTurnDriverTests
{
    [Fact]
    public void Select_UsesFirstActiveOpposingInvestigatorInCanonicalOrder()
    {
        var state = CreateState(
            order: ["opponent:0", "character:inactive", "character:second", "character:first"],
            participants:
            [
                Npc("opponent:0"),
                Investigator("character:inactive", active: false),
                Investigator("character:second"),
                Investigator("character:first")
            ]);

        var decision = new NpcCombatTurnDriver().Select(CombatContinuationStateValidator.ValidateNpcTurn(state));

        Assert.Equal(NpcCombatActionKind.BeginOpposedExchange, decision.Kind);
        Assert.Equal("opponent:0", decision.NpcParticipantId);
        Assert.Equal("character:second", decision.TargetParticipantId);
    }

    [Fact]
    public void Select_IsStableAndDoesNotMutateTheValidatedInput()
    {
        var state = CreateState(
            order: ["opponent:0", "character:second", "character:first"],
            participants: [Npc("opponent:0"), Investigator("character:first"), Investigator("character:second")]);
        var session = state.Combat!;
        var driver = new NpcCombatTurnDriver();
        var turn = CombatContinuationStateValidator.ValidateNpcTurn(state);

        var first = driver.Select(turn);
        var second = driver.Select(turn);

        Assert.Equal(first, second);
        Assert.Same(session, state.Combat);
        Assert.Equal(["opponent:0", "character:second", "character:first"], session.Order.Select(id => id.Value));
    }

    [Fact]
    public void Select_UsesPassForAnAlreadyValidatedEmptyLegalTargetSet()
    {
        var state = CreateState(
            order: ["opponent:0", "character:inactive"],
            participants: [Npc("opponent:0"), Investigator("character:inactive", active: false)]);
        var validated = new ValidatedNpcCombatTurn(state, state.Combat!, Npc("opponent:0"), []);

        var decision = new NpcCombatTurnDriver().Select(validated);

        Assert.Equal(NpcCombatActionKind.Pass, decision.Kind);
        Assert.Equal("opponent:0", decision.NpcParticipantId);
        Assert.Null(decision.TargetParticipantId);
    }

    [Fact]
    public void Select_TreatsAnActiveOwnerAsEligibleWithoutConnectionState()
    {
        var state = CreateState(
            order: ["opponent:0", "character:disconnected-owner"],
            participants: [Npc("opponent:0"), Investigator("character:disconnected-owner")]);

        var decision = new NpcCombatTurnDriver().Select(CombatContinuationStateValidator.ValidateNpcTurn(state));

        Assert.Equal(NpcCombatActionKind.BeginOpposedExchange, decision.Kind);
        Assert.Equal("character:disconnected-owner", decision.TargetParticipantId);
    }

    [Theory]
    [InlineData("duplicate-order")]
    [InlineData("missing-order-participant")]
    [InlineData("invalid-turn-index")]
    [InlineData("malformed-current-actor")]
    [InlineData("owned-npc")]
    [InlineData("character-on-npc")]
    [InlineData("side-kind-contradiction")]
    [InlineData("inconsistent-target-relation")]
    [InlineData("malformed-active-investigator")]
    [InlineData("no-active-investigator")]
    [InlineData("owner-character-identity-error")]
    public void ValidateNpcTurn_ThrowsForStructuralContradictions(string contradiction)
    {
        var state = contradiction switch
        {
            "duplicate-order" => CreateState(
                order: ["opponent:0", "character:first", "character:first"],
                participants: [Npc("opponent:0"), Investigator("character:first")]),
            "missing-order-participant" => CreateState(
                order: ["opponent:0", "character:missing"],
                participants: [Npc("opponent:0"), Investigator("character:first")]),
            "invalid-turn-index" => CreateState(
                order: ["opponent:0", "character:first"],
                participants: [Npc("opponent:0"), Investigator("character:first")], turnIndex: 2),
            "malformed-current-actor" => CreateState(
                order: ["character:first", "opponent:0"],
                participants: [Npc("opponent:0"), Investigator("character:first")]),
            "owned-npc" => CreateState(
                order: ["opponent:0", "character:first"],
                participants: [Npc("opponent:0") with { OwnerPlayerId = Guid.NewGuid() }, Investigator("character:first")]),
            "character-on-npc" => CreateState(
                order: ["opponent:0", "character:first"],
                participants: [Npc("opponent:0") with { CharacterId = Guid.NewGuid() }, Investigator("character:first")]),
            "side-kind-contradiction" => CreateState(
                order: ["opponent:0", "character:first"],
                participants: [Npc("opponent:0") with { Side = "investigator" }, Investigator("character:first")]),
            "inconsistent-target-relation" => CreateState(
                order: ["opponent:0", "character:first"],
                participants: [Npc("opponent:0"), Investigator("character:first") with { Side = "opponent" }]),
            "malformed-active-investigator" => CreateState(
                order: ["opponent:0", "character:first"],
                participants: [Npc("opponent:0"), Investigator("character:first") with { OwnerPlayerId = null }]),
            "no-active-investigator" => CreateState(
                order: ["opponent:0", "character:first"],
                participants: [Npc("opponent:0"), Investigator("character:first", active: false)]),
            "owner-character-identity-error" => CreateState(
                order: ["opponent:0", "character:first"],
                participants: [Npc("opponent:0"), Investigator("character:first")],
                characterOwnerMismatch: true),
            _ => throw new ArgumentOutOfRangeException(nameof(contradiction))
        };

        var exception = Assert.ThrowsAny<Exception>(() => CombatContinuationStateValidator.ValidateNpcTurn(state));

        Assert.Equal("NpcCombatContinuationInvariantException", exception.GetType().Name);
    }

    private static MultiplayerGameState CreateState(
        IReadOnlyList<string> order,
        IReadOnlyList<CombatParticipantState> participants,
        int turnIndex = 0,
        bool characterOwnerMismatch = false)
    {
        var characters = participants
            .Where(participant => participant.Kind == "investigator" && participant.CharacterId is not null && participant.OwnerPlayerId is not null)
            .Select(participant => new CharacterState(
                participant.CharacterId!.Value,
                participant.OwnerPlayerId!.Value,
                participant.Label,
                new Dictionary<string, int>(),
                new CharacterHealthState(10, 10, 50, false, false, false, false, [], null)))
            .ToArray();
        if (characterOwnerMismatch)
        {
            var character = characters[0];
            characters[0] = new CharacterState(
                character.CharacterId,
                Guid.NewGuid(),
                character.Name,
                character.CheckValues,
                character.Health,
                character.CombatLoadout);
        }
        var session = new CombatSession(
            Guid.NewGuid(), true, 1, turnIndex, order.Select(value => new CombatParticipantId(value)).ToArray(), participants,
            new Dictionary<string, int>(), new Dictionary<string, int>(), null, null, [],
            new Dictionary<string, DamageDispositionState>(), new Dictionary<Guid, DyingScheduleState>(), DateTimeOffset.UtcNow, null, null);

        return new MultiplayerGameState(Guid.NewGuid(), 1, MultiplayerGameStatus.Active, DateTimeOffset.UtcNow, characters, combat: session);
    }

    private static CombatParticipantState Npc(string participantId) => new(
        new CombatParticipantId(participantId), null, null, "NPC", "opponent", "opponent", 80, 50, 40, [], 0, true,
        new CombatDamageProfile(60, 50, new DamageBonusProfile(110, DamageBonusKind.Flat, 0, 0, 0, "+0", 0), new CombatWeaponProfile("unarmed", "Unarmed", new DiceExpression("1d3", 1, 3, 0), true, "melee_non_impaling"), 0),
        new OpponentVitalityState(10, 10), null);

    private static CombatParticipantState Investigator(string participantId, bool active = true, Guid? characterId = null)
    {
        var resolvedCharacterId = characterId ?? Guid.NewGuid();
        return new CombatParticipantState(
            new CombatParticipantId(participantId), resolvedCharacterId, Guid.NewGuid(), "Investigator", "investigator", "investigator", 60, 50, 40,
            [CombatResponse.Dodge], 1, active,
            new CombatDamageProfile(60, 50, new DamageBonusProfile(110, DamageBonusKind.Flat, 0, 0, 0, "+0", 0), new CombatWeaponProfile("unarmed", "Unarmed", new DiceExpression("1d3", 1, 3, 0), true, "melee_non_impaling"), 0),
            null, null);
    }
}
