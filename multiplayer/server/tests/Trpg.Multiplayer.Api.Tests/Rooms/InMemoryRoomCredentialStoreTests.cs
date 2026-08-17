using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Rooms;

public sealed class InMemoryRoomCredentialStoreTests
{
    private const string TestSecret = "TEST-ONLY-SECRET-DO-NOT-LEAK-123456";

    [Fact]
    public void Set_StoresCredentialForInternalLookup()
    {
        var store = new InMemoryRoomCredentialStore();
        var roomId = Guid.NewGuid();

        store.Set(roomId, TestSecret);

        Assert.True(store.Exists(roomId));
        Assert.True(store.TryGet(roomId, out var credential));
        Assert.Equal(TestSecret, credential);
    }

    [Fact]
    public void Set_ReplacesExistingCredentialWithoutRetainingOldValue()
    {
        var store = new InMemoryRoomCredentialStore();
        var roomId = Guid.NewGuid();
        store.Set(roomId, "old-test-secret");

        store.Set(roomId, TestSecret);

        Assert.True(store.TryGet(roomId, out var credential));
        Assert.Equal(TestSecret, credential);
        Assert.NotEqual("old-test-secret", credential);
    }

    [Fact]
    public void Remove_DeletesCredentialAndReportsAbsence()
    {
        var store = new InMemoryRoomCredentialStore();
        var roomId = Guid.NewGuid();
        store.Set(roomId, TestSecret);

        var removed = store.Remove(roomId);

        Assert.True(removed);
        Assert.False(store.Exists(roomId));
        Assert.False(store.TryGet(roomId, out var credential));
        Assert.Null(credential);
        Assert.False(store.Remove(roomId));
    }
}
