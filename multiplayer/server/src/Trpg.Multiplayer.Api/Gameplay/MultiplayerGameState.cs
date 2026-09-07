using System.Collections.ObjectModel;

namespace Trpg.Multiplayer.Api.Gameplay;

public enum MultiplayerGameStatus
{
    Active
}

public sealed record CharacterCombatLoadout(CombatWeaponProfile Weapon, int FixedArmor);

public sealed class CharacterState
{
    public CharacterState(
        Guid characterId,
        Guid ownerPlayerId,
        string name,
        IReadOnlyDictionary<string, int> checkValues,
        CharacterHealthState health)
        : this(
            characterId,
            ownerPlayerId,
            name,
            checkValues,
            health,
            CreateDefaultCombatLoadout())
    {
    }

    public CharacterState(
        Guid characterId,
        Guid ownerPlayerId,
        string name,
        IReadOnlyDictionary<string, int> checkValues,
        CharacterHealthState health,
        CharacterCombatLoadout combatLoadout)
    {
        CharacterId = characterId;
        OwnerPlayerId = ownerPlayerId;
        Name = name;
        CheckValues = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(checkValues, StringComparer.OrdinalIgnoreCase));
        Health = health;
        CombatLoadout = combatLoadout;
    }

    public Guid CharacterId { get; }

    public Guid OwnerPlayerId { get; }

    public string Name { get; }

    // Provisional Phase 2A rule-value container. It is intentionally narrow and strongly typed.
    public IReadOnlyDictionary<string, int> CheckValues { get; }

    public CharacterHealthState Health { get; }

    public CharacterCombatLoadout CombatLoadout { get; }

    public CharacterState WithHealth(CharacterHealthState health) => new(
        CharacterId,
        OwnerPlayerId,
        Name,
        CheckValues,
        health,
        CombatLoadout);

    private static CharacterCombatLoadout CreateDefaultCombatLoadout() => new(
        CocCombatDamageRules.NormalizeWeapon(
            "unarmed",
            "徒手/拳脚",
            "1d3",
            addsDamageBonus: true,
            "melee_non_impaling"),
        FixedArmor: 0);
}

public sealed class MultiplayerGameState
{
    public MultiplayerGameState(
        Guid roomId,
        long revision,
        MultiplayerGameStatus status,
        DateTimeOffset createdAt,
        IEnumerable<CharacterState> characters,
        GameCheckRecord? lastCheck = null,
        CombatSession? combat = null)
    {
        RoomId = roomId;
        Revision = revision;
        Status = status;
        CreatedAt = createdAt;
        Characters = new ReadOnlyCollection<CharacterState>((characters ?? []).ToArray());
        LastCheck = lastCheck;
        Combat = combat;
    }

    public Guid RoomId { get; }

    public long Revision { get; }

    public MultiplayerGameStatus Status { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<CharacterState> Characters { get; }

    public GameCheckRecord? LastCheck { get; }

    public CombatSession? Combat { get; }
}
