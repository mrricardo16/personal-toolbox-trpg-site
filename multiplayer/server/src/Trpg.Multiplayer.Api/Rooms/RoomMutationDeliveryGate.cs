using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class RoomMutationDeliveryGate
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> roomGates = new();

    // 修改时间：2026-08-17 10:53:43
    // 修改说明：同一房间的 HTTP mutation 与对应 notifier 投递共享异步 gate。
    // 修改原因：避免较高 revision 在较低 revision 的 notifier 尚未完成时先行投递。
    // 业务影响：仅串行化同房间 Join/Ready/Leave；不同房间仍可并发。
    public async Task<T> RunAsync<T>(Guid roomId, Func<Task<T>> operation)
    {
        var roomGate = roomGates.GetOrAdd(roomId, static _ => new SemaphoreSlim(1, 1));
        await roomGate.WaitAsync();
        try
        {
            return await operation();
        }
        finally
        {
            roomGate.Release();
        }
    }
}
