namespace Trpg.Multiplayer.Api.Gameplay;

public interface IGameCoordinator
{
    Task<GameResult<MultiplayerGameState>> InitializeAsync(InitializeGameCommand command);

    Task<GameResult<GameSnapshot>> GetProjectionAsync(Guid roomId, Guid viewerPlayerId);

    Task<GameResult<CharacterState>> GetCharacterForOwnerAsync(Guid roomId, Guid characterId, Guid playerId);

    Task<GameResult<GameCheckResult>> ResolveCheckAsync(ResolveCheckCommand command);

    Task<bool> RemoveAsync(Guid roomId);
}
