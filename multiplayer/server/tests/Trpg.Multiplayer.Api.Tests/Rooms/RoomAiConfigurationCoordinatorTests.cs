using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Rooms;

public sealed class RoomAiConfigurationCoordinatorTests
{
    private const string TestSecret = "TEST-ONLY-SECRET-DO-NOT-LEAK-123456";

    [Fact]
    public async Task SetAiConfigurationAsync_HostStoresCredentialAndPublicConfiguration()
    {
        var (coordinator, credentials, room) = await CreateRoomAsync();

        var result = await coordinator.SetAiConfigurationAsync(new SetRoomAiConfigurationCommand(
            room.RoomId,
            room.HostPlayerId,
            "deepseek",
            endpoint: null,
            "deepseek-chat",
            TestSecret));

        Assert.True(result.IsSuccess);
        Assert.True(result.Changed);
        Assert.Equal(2, result.Value!.Revision);
        Assert.Equal(
            new RoomAiConfiguration(
                "deepseek",
                "https://api.deepseek.com/v1/chat/completions",
                "deepseek-chat",
                true),
            result.Value.AiConfiguration);
        Assert.True(credentials.TryGet(room.RoomId, out var storedCredential));
        Assert.Equal(TestSecret, storedCredential);
    }

    [Fact]
    public async Task SetAiConfigurationAsync_ExactPublicConfigurationWithoutNewCredentialIsIdempotent()
    {
        var (coordinator, _, room) = await CreateRoomAsync();
        var configured = await ConfigureAsync(coordinator, room, TestSecret);

        var repeated = await ConfigureAsync(coordinator, configured.Value!, credential: null);

        Assert.True(repeated.IsSuccess);
        Assert.False(repeated.Changed);
        Assert.Equal(2, repeated.Value!.Revision);
    }

    [Fact]
    public async Task SetAiConfigurationAsync_NewCredentialReplacesOldAndIncrementsRevision()
    {
        var (coordinator, credentials, room) = await CreateRoomAsync();
        var configured = await ConfigureAsync(coordinator, room, "old-test-secret");

        var replaced = await ConfigureAsync(coordinator, configured.Value!, TestSecret);

        Assert.True(replaced.IsSuccess);
        Assert.True(replaced.Changed);
        Assert.Equal(3, replaced.Value!.Revision);
        Assert.True(credentials.TryGet(room.RoomId, out var storedCredential));
        Assert.Equal(TestSecret, storedCredential);
        Assert.NotEqual("old-test-secret", storedCredential);
    }

    [Fact]
    public async Task RemoveAiCredentialAsync_RetainsPublicProviderEndpointAndModel()
    {
        var (coordinator, credentials, room) = await CreateRoomAsync();
        var configured = await ConfigureAsync(coordinator, room, TestSecret);

        var removed = await coordinator.RemoveAiCredentialAsync(
            new RemoveRoomAiCredentialCommand(room.RoomId, room.HostPlayerId));
        var repeated = await coordinator.RemoveAiCredentialAsync(
            new RemoveRoomAiCredentialCommand(room.RoomId, room.HostPlayerId));

        Assert.True(removed.IsSuccess);
        Assert.True(removed.Changed);
        Assert.Equal(3, removed.Value!.Revision);
        Assert.Equal(configured.Value!.AiConfiguration! with { CredentialPresent = false }, removed.Value.AiConfiguration);
        Assert.False(credentials.Exists(room.RoomId));
        Assert.True(repeated.IsSuccess);
        Assert.False(repeated.Changed);
        Assert.Equal(3, repeated.Value!.Revision);
    }

    [Fact]
    public async Task AiConfigurationMutations_RejectNonHostAndUnknownRoom()
    {
        var (coordinator, _, room) = await CreateRoomAsync();
        var memberId = Guid.NewGuid();
        var joined = await coordinator.JoinAsync(new JoinRoomCommand(room.RoomId, memberId, "Member"));
        Assert.True(joined.IsSuccess);

        var nonHost = await ConfigureAsync(coordinator, joined.Value!, TestSecret, memberId);
        var unknown = await coordinator.SetAiConfigurationAsync(new SetRoomAiConfigurationCommand(
            Guid.NewGuid(),
            room.HostPlayerId,
            "deepseek",
            endpoint: null,
            "deepseek-chat",
            TestSecret));

        AssertError(nonHost, RoomErrorCode.NotHost);
        AssertError(unknown, RoomErrorCode.RoomNotFound);
    }

    [Fact]
    public async Task MemberLeave_PreservesCredentialButHostLeaveRemovesIt()
    {
        var (coordinator, credentials, room) = await CreateRoomAsync();
        var configured = await ConfigureAsync(coordinator, room, TestSecret);
        var memberId = Guid.NewGuid();
        var joined = await coordinator.JoinAsync(
            new JoinRoomCommand(room.RoomId, memberId, "Member"));
        Assert.True(joined.IsSuccess);

        var memberLeave = await coordinator.LeaveAsync(
            new LeaveRoomCommand(room.RoomId, memberId));

        Assert.True(memberLeave.IsSuccess);
        Assert.False(memberLeave.Value!.RoomWasClosed);
        Assert.True(credentials.Exists(room.RoomId));

        var hostLeave = await coordinator.LeaveAsync(
            new LeaveRoomCommand(room.RoomId, configured.Value!.HostPlayerId));

        Assert.True(hostLeave.IsSuccess);
        Assert.True(hostLeave.Value!.RoomWasClosed);
        Assert.False(credentials.Exists(room.RoomId));
    }

    private static async Task<(RoomCoordinator Coordinator, InMemoryRoomCredentialStore Credentials, RoomSession Room)> CreateRoomAsync()
    {
        var credentials = new InMemoryRoomCredentialStore();
        var coordinator = new RoomCoordinator(new InMemoryRoomStore(), credentials);
        var created = await coordinator.CreateAsync(
            new CreateRoomCommand(Guid.NewGuid(), "Host", 4));
        return (coordinator, credentials, Assert.IsType<RoomSession>(created.Value));
    }

    private static Task<RoomResult<RoomSession>> ConfigureAsync(
        RoomCoordinator coordinator,
        RoomSession room,
        string? credential,
        Guid? playerId = null)
    {
        return coordinator.SetAiConfigurationAsync(new SetRoomAiConfigurationCommand(
            room.RoomId,
            playerId ?? room.HostPlayerId,
            "deepseek",
            endpoint: null,
            "deepseek-chat",
            credential));
    }

    private static void AssertError<T>(RoomResult<T> result, RoomErrorCode expectedCode)
    {
        Assert.False(result.IsSuccess);
        Assert.Equal(expectedCode, result.Error!.Code);
    }
}
