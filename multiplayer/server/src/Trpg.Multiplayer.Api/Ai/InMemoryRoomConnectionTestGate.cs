using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Ai;

public sealed class InMemoryRoomConnectionTestGate : IRoomConnectionTestGate
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> roomSemaphores = new();

    public bool TryEnter(Guid roomId, out IDisposable? lease)
    {
        var semaphore = roomSemaphores.GetOrAdd(roomId, _ => new SemaphoreSlim(1, 1));
        if (!semaphore.Wait(0))
        {
            lease = null;
            return false;
        }

        lease = new SemaphoreLease(semaphore);
        return true;
    }

    private sealed class SemaphoreLease(SemaphoreSlim semaphore) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
