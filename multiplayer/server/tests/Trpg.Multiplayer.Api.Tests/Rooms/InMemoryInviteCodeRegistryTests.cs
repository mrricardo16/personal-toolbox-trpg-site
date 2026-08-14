using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Rooms;

public sealed class InMemoryInviteCodeRegistryTests
{
    [Fact]
    public void TryRegister_RetriesAfterCollisionAndKeepsBothRoomMappings()
    {
        var registry = new InMemoryInviteCodeRegistry(new SequenceInviteCodeGenerator("COLLIDE", "COLLIDE", "UNIQUE1"));
        var firstRoomId = Guid.NewGuid();
        var secondRoomId = Guid.NewGuid();

        Assert.True(registry.TryRegister(firstRoomId, out var firstCode));
        Assert.True(registry.TryRegister(secondRoomId, out var secondCode));

        Assert.Equal("COLLIDE", firstCode);
        Assert.Equal("UNIQUE1", secondCode);
        Assert.True(registry.TryGetRoomId("COLLIDE", out var loadedFirstRoomId));
        Assert.True(registry.TryGetRoomId("UNIQUE1", out var loadedSecondRoomId));
        Assert.Equal(firstRoomId, loadedFirstRoomId);
        Assert.Equal(secondRoomId, loadedSecondRoomId);
    }

    private sealed class SequenceInviteCodeGenerator(params string[] codes) : IInviteCodeGenerator
    {
        private readonly Queue<string> codes = new(codes);

        public string Generate() => codes.Dequeue();
    }
}
