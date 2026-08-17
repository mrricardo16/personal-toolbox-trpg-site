namespace Trpg.Multiplayer.Api.Rooms;

public sealed record RoomError(RoomErrorCode Code);

public sealed class RoomResult<T>
{
    private RoomResult(T? value, RoomError? error, bool changed)
    {
        Value = value;
        Error = error;
        Changed = changed;
    }

    public T? Value { get; }

    public RoomError? Error { get; }

    public bool IsSuccess => Error is null;

    public bool Changed { get; }

    public static RoomResult<T> Success(T value, bool changed = true) => new(value, null, changed);

    public static RoomResult<T> Failure(RoomErrorCode errorCode) => new(default, new RoomError(errorCode), false);
}
