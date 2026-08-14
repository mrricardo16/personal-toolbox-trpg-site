using System.Collections.Concurrent;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class InMemoryInviteCodeRegistry(IInviteCodeGenerator inviteCodeGenerator) : IInviteCodeRegistry
{
    private const int MaxCollisionRetries = 16;
    private readonly ConcurrentDictionary<string, Guid> roomIdsByCode = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, string> codesByRoomId = new();

    public bool TryRegister(Guid roomId, out string inviteCode)
    {
        for (var attempt = 0; attempt < MaxCollisionRetries; attempt++)
        {
            var candidate = inviteCodeGenerator.Generate();
            if (!string.IsNullOrWhiteSpace(candidate) && roomIdsByCode.TryAdd(candidate, roomId))
            {
                codesByRoomId[roomId] = candidate;
                inviteCode = candidate;
                return true;
            }
        }

        inviteCode = string.Empty;
        return false;
    }

    public bool TryGetRoomId(string inviteCode, out Guid roomId) => roomIdsByCode.TryGetValue(inviteCode, out roomId);

    public bool TryGetInviteCode(Guid roomId, out string? inviteCode) => codesByRoomId.TryGetValue(roomId, out inviteCode);

    public void Remove(Guid roomId)
    {
        if (codesByRoomId.TryRemove(roomId, out var inviteCode))
        {
            roomIdsByCode.TryRemove(inviteCode, out _);
        }
    }
}
