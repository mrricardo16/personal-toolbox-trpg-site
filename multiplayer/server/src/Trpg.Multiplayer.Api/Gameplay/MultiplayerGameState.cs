using System.Collections.ObjectModel;

namespace Trpg.Multiplayer.Api.Gameplay;

public enum MultiplayerGameStatus
{
    Active
}

public sealed class CharacterState
{
    public CharacterState(
        Guid characterId,
        Guid ownerPlayerId,
        string name,
        IReadOnlyDictionary<string, int> checkValues)
    {
        CharacterId = characterId;
        OwnerPlayerId = ownerPlayerId;
        Name = name;
        CheckValues = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int>(checkValues, StringComparer.OrdinalIgnoreCase));
    }

    public Guid CharacterId { get; }

    public Guid OwnerPlayerId { get; }

    public string Name { get; }

    // Provisional Phase 2A rule-value container. It is intentionally narrow and strongly typed.
    public IReadOnlyDictionary<string, int> CheckValues { get; }
}

public sealed class MultiplayerGameState
{
    public MultiplayerGameState(
        Guid roomId,
        long revision,
        MultiplayerGameStatus status,
        DateTimeOffset createdAt,
        IEnumerable<CharacterState> characters,
        GameCheckRecord? lastCheck = null)
    {
        RoomId = roomId;
        Revision = revision;
        Status = status;
        CreatedAt = createdAt;
        Characters = new ReadOnlyCollection<CharacterState>((characters ?? []).ToArray());
        LastCheck = lastCheck;
    }

    public Guid RoomId { get; }

    public long Revision { get; }

    public MultiplayerGameStatus Status { get; }

    public DateTimeOffset CreatedAt { get; }

    public IReadOnlyList<CharacterState> Characters { get; }

    public GameCheckRecord? LastCheck { get; }
}
