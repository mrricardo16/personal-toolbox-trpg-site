using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Rooms;

public sealed class RoomStoreRegistrationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void RoomStore_IsRegisteredAsSingleton()
    {
        var first = factory.Services.GetRequiredService<IRoomStore>();
        var second = factory.Services.GetRequiredService<IRoomStore>();

        Assert.IsType<InMemoryRoomStore>(first);
        Assert.Same(first, second);
    }
}
