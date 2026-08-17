namespace Trpg.Multiplayer.Api.Gameplay;

public sealed record InitializeGameCommand(
    Guid RoomId,
    Guid HostPlayerId,
    IReadOnlyList<InitializeCharacterCommand> Characters);

public sealed record InitializeCharacterCommand(
    Guid PlayerId,
    string Name,
    IReadOnlyDictionary<string, int> CheckValues);

public sealed record GameError(GameErrorCode Code);

public enum GameErrorCode
{
    RoomNotFound,
    RoomClosed,
    NotMember,
    NotHost,
    InvalidRoster,
    UnknownPlayer,
    DuplicateCharacterOwnership,
    AlreadyInitialized,
    GameNotFound,
    CharacterNotFound,
    CharacterNotOwned,
    InvalidCheckKey
}

public sealed class GameResult<T>
{
    private GameResult(T? value, GameError? error, bool changed)
    {
        Value = value;
        Error = error;
        Changed = changed;
    }

    public T? Value { get; }

    public GameError? Error { get; }

    public bool IsSuccess => Error is null;

    public bool Changed { get; }

    public static GameResult<T> Success(T value, bool changed = true) => new(value, null, changed);

    public static GameResult<T> Failure(GameErrorCode code) => new(default, new GameError(code), false);
}
