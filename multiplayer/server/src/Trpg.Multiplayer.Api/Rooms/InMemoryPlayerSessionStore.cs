using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Trpg.Multiplayer.Api.Rooms;

public sealed class InMemoryPlayerSessionStore : IPlayerSessionStore
{
    private const int TokenByteLength = 32;
    private readonly ConcurrentDictionary<string, PlayerSessionContext> sessions = new(StringComparer.Ordinal);

    public string Create(Guid playerId, Guid roomId, bool isHost)
    {
        while (true)
        {
            var token = Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenByteLength));
            if (sessions.TryAdd(token, new PlayerSessionContext(playerId, roomId, isHost)))
            {
                return token;
            }
        }
    }

    public bool TryGet(string token, out PlayerSessionContext? session) => sessions.TryGetValue(token, out session);

    public void Remove(string token) => sessions.TryRemove(token, out _);

    public int RemoveByRoom(Guid roomId)
    {
        var removed = 0;
        foreach (var session in sessions)
        {
            if (session.Value.RoomId == roomId && sessions.TryRemove(session.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}
