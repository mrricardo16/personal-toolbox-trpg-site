using System.Collections.Concurrent;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Rooms;

public sealed class InMemoryRoomStoreTests
{
    [Fact]
    public void TryAddThenTryGet_ReturnsTheSameRoom()
    {
        var store = new InMemoryRoomStore();
        var room = NewRoom(Guid.NewGuid());

        Assert.True(store.TryAdd(room));
        Assert.True(store.TryGet(room.RoomId, out var loaded));
        Assert.Same(room, loaded);
    }

    [Fact]
    public void TryGetUnknownRoom_ReturnsFalseWithoutThrowing()
    {
        var store = new InMemoryRoomStore();

        Assert.False(store.TryGet(Guid.NewGuid(), out _));
    }

    [Fact]
    public void TryRemove_RemovesRoomAndReturnsRemovedValue()
    {
        var store = new InMemoryRoomStore();
        var room = NewRoom(Guid.NewGuid());
        store.TryAdd(room);

        Assert.True(store.TryRemove(room.RoomId, out var removed));
        Assert.Same(room, removed);
        Assert.False(store.TryGet(room.RoomId, out _));
    }

    [Fact]
    public void Exists_TracksRoomPresence()
    {
        var store = new InMemoryRoomStore();
        var room = NewRoom(Guid.NewGuid());

        Assert.False(store.Exists(room.RoomId));
        Assert.True(store.TryAdd(room));
        Assert.True(store.Exists(room.RoomId));
        Assert.True(store.TryRemove(room.RoomId, out _));
        Assert.False(store.Exists(room.RoomId));
    }

    [Fact]
    public void TryAddDuplicateRoomId_ReturnsFalseAndPreservesOriginal()
    {
        var store = new InMemoryRoomStore();
        var roomId = Guid.NewGuid();
        var first = NewRoom(roomId);
        var duplicate = NewRoom(roomId);

        Assert.True(store.TryAdd(first));
        Assert.False(store.TryAdd(duplicate));
        Assert.True(store.TryGet(roomId, out var loaded));
        Assert.Same(first, loaded);
    }

    [Fact]
    public void RemovingOneRoom_DoesNotAffectAnotherRoom()
    {
        var store = new InMemoryRoomStore();
        var roomA = NewRoom(Guid.NewGuid());
        var roomB = NewRoom(Guid.NewGuid());
        store.TryAdd(roomA);
        store.TryAdd(roomB);

        Assert.True(store.TryRemove(roomA.RoomId, out _));
        Assert.False(store.TryGet(roomA.RoomId, out _));
        Assert.True(store.TryGet(roomB.RoomId, out var remaining));
        Assert.Same(roomB, remaining);
    }

    [Fact]
    public void ConcurrentWritesToDifferentRooms_PreserveEveryRoom()
    {
        var store = new InMemoryRoomStore();
        var rooms = Enumerable.Range(0, 256)
            .Select(_ => NewRoom(Guid.NewGuid()))
            .ToArray();
        var results = new ConcurrentBag<bool>();

        Parallel.ForEach(rooms, room => results.Add(store.TryAdd(room)));

        Assert.Equal(rooms.Length, results.Count);
        Assert.All(results, Assert.True);
        Assert.All(rooms, room =>
        {
            Assert.True(store.TryGet(room.RoomId, out var loaded));
            Assert.Same(room, loaded);
        });
    }

    private static RoomSession NewRoom(Guid roomId)
    {
        return new RoomSession(
            roomId,
            Guid.NewGuid(),
            4,
            RoomStatus.Lobby,
            0,
            DateTimeOffset.UtcNow,
            [new RoomPlayer(Guid.NewGuid(), "host")]);
    }
}
