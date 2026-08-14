namespace Trpg.Multiplayer.Api.Rooms;

public sealed record RoomError(RoomErrorCode Code);

public sealed class RoomResult<T>
{
    private RoomResult(T? value, RoomError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public RoomError? Error { get; }

    public bool IsSuccess => Error is null;

    public static RoomResult<T> Success(T value) => new(value, null);

    public static RoomResult<T> Failure(RoomErrorCode errorCode) => new(default, new RoomError(errorCode));
}
