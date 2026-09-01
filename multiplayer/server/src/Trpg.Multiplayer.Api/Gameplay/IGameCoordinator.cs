namespace Trpg.Multiplayer.Api.Gameplay;

public interface IGameCoordinator
{
    Task<GameResult<MultiplayerGameState>> InitializeAsync(InitializeGameCommand command);

    Task<GameResult<GameSnapshot>> GetProjectionAsync(Guid roomId, Guid viewerPlayerId);

    Task<GameResult<CharacterState>> GetCharacterForOwnerAsync(Guid roomId, Guid characterId, Guid playerId);

    Task<GameResult<GameCheckResult>> ResolveCheckAsync(ResolveCheckCommand command);

    Task<GameResult<HpDamageResult>> ApplyDamageAsync(ApplyDamageCommand command);

    Task<GameResult<HealthStabilizationResult>> ResolveDyingRoundAsync(ResolveDyingRoundCommand command);

    Task<GameResult<HealthStabilizationResult>> ResolveFirstAidAsync(ResolveFirstAidCommand command);

    Task<bool> RemoveAsync(Guid roomId);
}

internal interface IInternalCombatCoordinator
{
    Task<GameResult<StartCombatResult>> StartCombatAsync(StartCombatCommand command);

    Task<GameResult<BeginOpposedExchangeResult>> BeginOpposedExchangeAsync(BeginOpposedExchangeCommand command);
}

internal interface IInternalCombatResolutionCoordinator : IInternalCombatCoordinator
{
    Task<GameResult<ResolvePendingExchangeResult>> ResolvePendingExchangeAsync(ResolvePendingExchangeCommand command);

    Task<GameResult<PassCombatTurnResult>> PassCombatTurnAsync(PassCombatTurnCommand command);

    Task<GameResult<EndCombatResult>> EndCombatAsync(EndCombatCommand command);
}
