namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record CharacterSnapshot(
    Guid CharacterId,
    Guid OwnerPlayerId,
    string Name,
    IReadOnlyDictionary<string, int> CheckValues,
    CharacterHealthSnapshot? Health);

public sealed record CharacterHealthSnapshot(int CurrentHp, int MaxHp, bool MajorWound, bool Unconscious, bool Dying, bool Dead);

public sealed record GameSnapshot(
    Guid RoomId,
    long Revision,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<CharacterSnapshot> Characters,
    GameCheckRecord? LastCheck = null);

public static class GameProjection
{
    public static GameSnapshot Build(MultiplayerGameState state, Guid viewerPlayerId)
    {
        return new GameSnapshot(
            state.RoomId,
            state.Revision,
            state.Status.ToString(),
            state.CreatedAt,
            state.Characters
                .Select(character => new CharacterSnapshot(
                    character.CharacterId,
                    character.OwnerPlayerId,
                    character.Name,
                    character.OwnerPlayerId == viewerPlayerId
                        ? new Dictionary<string, int>(character.CheckValues, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    character.OwnerPlayerId == viewerPlayerId
                        ? new CharacterHealthSnapshot(
                            character.Health.CurrentHp,
                            character.Health.MaxHp,
                            character.Health.MajorWound,
                            character.Health.Unconscious,
                            character.Health.Dying,
                            character.Health.Dead)
                        : null))
                .ToArray(),
            state.LastCheck);
    }
}
