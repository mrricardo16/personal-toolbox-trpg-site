using System.Text.Json;
using Trpg.Multiplayer.Api.Realtime;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Realtime;

public sealed class PlayerConnectionRegistryTests
{
    [Fact]
    public void Register_FirstConnection_ReportsFirstActiveConnection()
    {
        var registry = new InMemoryPlayerConnectionRegistry();
        var roomId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var isFirstConnection = registry.Register("connection-1", roomId, playerId);

        Assert.True(isFirstConnection);
        Assert.Equal(["connection-1"], registry.GetConnections(roomId, playerId));
    }

    [Fact]
    public void Register_DuplicateConnection_IsIdempotent()
    {
        var registry = new InMemoryPlayerConnectionRegistry();
        var roomId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        var isFirstConnection = registry.Register("connection-1", roomId, playerId);
        var duplicateIsFirstConnection = registry.Register("connection-1", roomId, playerId);

        Assert.True(isFirstConnection);
        Assert.False(duplicateIsFirstConnection);
        Assert.Equal(["connection-1"], registry.GetConnections(roomId, playerId));
    }

    [Fact]
    public void Register_MultipleConnectionsForPlayer_TracksEachConnection()
    {
        var registry = new InMemoryPlayerConnectionRegistry();
        var roomId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        registry.Register("connection-1", roomId, playerId);
        var secondIsFirstConnection = registry.Register("connection-2", roomId, playerId);

        Assert.False(secondIsFirstConnection);
        Assert.Equal(
            ["connection-1", "connection-2"],
            registry.GetConnections(roomId, playerId).OrderBy(connectionId => connectionId));
    }

    [Fact]
    public void Unregister_ReportsLastConnectionOnlyWhenPlayerHasNoConnectionsLeft()
    {
        var registry = new InMemoryPlayerConnectionRegistry();
        var roomId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        registry.Register("connection-1", roomId, playerId);
        registry.Register("connection-2", roomId, playerId);

        var firstRemoval = registry.Unregister("connection-1");
        var lastRemoval = registry.Unregister("connection-2");

        Assert.NotNull(firstRemoval);
        Assert.False(firstRemoval.IsLastConnection);
        Assert.Equal(new ConnectionIdentity("connection-1", roomId, playerId), firstRemoval.Identity);
        Assert.NotNull(lastRemoval);
        Assert.True(lastRemoval.IsLastConnection);
        Assert.Equal(new ConnectionIdentity("connection-2", roomId, playerId), lastRemoval.Identity);
    }

    [Fact]
    public void GetRoomConnections_ReturnsOnlyConnectionsInRequestedRoom()
    {
        var registry = new InMemoryPlayerConnectionRegistry();
        var firstRoomId = Guid.NewGuid();
        var secondRoomId = Guid.NewGuid();
        var firstPlayerId = Guid.NewGuid();
        var secondPlayerId = Guid.NewGuid();
        registry.Register("connection-1", firstRoomId, firstPlayerId);
        registry.Register("connection-2", firstRoomId, secondPlayerId);
        registry.Register("connection-3", secondRoomId, firstPlayerId);

        var roomConnections = registry.GetRoomConnections(firstRoomId);

        Assert.Equal(2, roomConnections.Count);
        Assert.Contains(new ConnectionIdentity("connection-1", firstRoomId, firstPlayerId), roomConnections);
        Assert.Contains(new ConnectionIdentity("connection-2", firstRoomId, secondPlayerId), roomConnections);
        Assert.DoesNotContain(roomConnections, identity => identity.ConnectionId == "connection-3");
    }

    [Fact]
    public void Unregister_UnknownConnection_ReturnsNull()
    {
        var registry = new InMemoryPlayerConnectionRegistry();

        Assert.Null(registry.Unregister("unknown-connection"));
    }

    [Fact]
    public void RemoveRoom_RemovesEveryRoomConnectionAndLeavesOtherRoomsUntouched()
    {
        var registry = new InMemoryPlayerConnectionRegistry();
        var removedRoomId = Guid.NewGuid();
        var retainedRoomId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        registry.Register("connection-1", removedRoomId, playerId);
        registry.Register("connection-2", removedRoomId, Guid.NewGuid());
        registry.Register("connection-3", retainedRoomId, playerId);

        var removed = registry.RemoveRoom(removedRoomId);

        Assert.Equal(2, removed.Count);
        Assert.Empty(registry.GetRoomConnections(removedRoomId));
        Assert.Null(registry.Unregister("connection-1"));
        Assert.Equal(["connection-3"], registry.GetConnections(retainedRoomId, playerId));
    }

    [Fact]
    public void PublicResults_WhenSerialized_DoNotExposeSessionTokens()
    {
        var registry = new InMemoryPlayerConnectionRegistry();
        var roomId = Guid.NewGuid();
        var playerId = Guid.NewGuid();

        registry.Register("connection-1", roomId, playerId);
        var roomConnections = registry.GetRoomConnections(roomId);
        var removal = registry.Unregister("connection-1");

        object[] publicResults = [roomConnections, removal!];
        foreach (var result in publicResults)
        {
            var serialized = JsonSerializer.Serialize(result, result.GetType());
            Assert.DoesNotContain("Token", serialized, StringComparison.OrdinalIgnoreCase);
        }
    }
}
