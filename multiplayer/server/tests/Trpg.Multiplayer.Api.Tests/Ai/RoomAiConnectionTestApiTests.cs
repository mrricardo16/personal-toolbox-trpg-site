using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Trpg.Multiplayer.Api.Ai;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Ai;

public sealed class RoomAiConnectionTestApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task TestEndpoint_RequiresHostAndReturnsMissingCredentialWithoutChangingRevision()
    {
        var host = await CreateRoomAsync("Host");
        var member = await JoinRoomAsync(host.InviteCode, "Member");
        await ConfigurePublicMetadataAsync(host);
        var before = GetRoom(host.RoomId).Revision;

        using var invalid = await SendAsync(
            host.RoomId,
            "not-a-token");
        using var nonHost = await SendAsync(host.RoomId, member.PlayerSessionToken);
        using var hostResult = await SendAsync(host.RoomId, host.PlayerSessionToken);

        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, nonHost.StatusCode);
        Assert.Equal(HttpStatusCode.OK, hostResult.StatusCode);
        var result = await hostResult.Content.ReadFromJsonAsync<AiConnectionTestResult>();
        Assert.False(result!.Success);
        Assert.Equal("CREDENTIAL_MISSING", result.Code);
        Assert.Equal(before, GetRoom(host.RoomId).Revision);
    }

    [Fact]
    public async Task TestEndpoint_RejectsMissingConfigurationAndUnknownRoomSafely()
    {
        var host = await CreateRoomAsync("Host");
        using var missingConfiguration = await SendAsync(host.RoomId, host.PlayerSessionToken);
        Assert.Equal(HttpStatusCode.OK, missingConfiguration.StatusCode);
        var result = await missingConfiguration.Content.ReadFromJsonAsync<AiConnectionTestResult>();
        Assert.Equal("CONFIGURATION_MISSING", result!.Code);

        var missingRoomId = Guid.NewGuid();
        var token = factory.Services.GetRequiredService<IPlayerSessionStore>()
            .Create(Guid.NewGuid(), missingRoomId, isHost: true);
        using var missingRoom = await SendAsync(missingRoomId, token);
        Assert.Equal(HttpStatusCode.NotFound, missingRoom.StatusCode);
    }

    private async Task<RoomCreatedResponse> CreateRoomAsync(string nickname)
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/rooms",
            new { nickname, maxPlayers = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<RoomCreatedResponse>(await response.Content.ReadFromJsonAsync<RoomCreatedResponse>());
    }

    private async Task<RoomJoinedResponse> JoinRoomAsync(string inviteCode, string nickname)
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/rooms/join",
            new { inviteCode, nickname });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<RoomJoinedResponse>(await response.Content.ReadFromJsonAsync<RoomJoinedResponse>());
    }

    private async Task ConfigurePublicMetadataAsync(RoomCreatedResponse room)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/rooms/{room.RoomId}/ai-config")
        {
            Content = JsonContent.Create(new { provider = "deepseek", model = "deepseek-chat" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", room.PlayerSessionToken);
        using var response = await factory.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(Guid roomId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomId}/ai-config/test");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return factory.CreateClient().SendAsync(request);
    }

    private RoomSession GetRoom(Guid roomId)
    {
        Assert.True(factory.Services.GetRequiredService<IRoomStore>().TryGet(roomId, out var room));
        return Assert.IsType<RoomSession>(room);
    }

    private sealed record RoomCreatedResponse(Guid RoomId, string InviteCode, Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
    private sealed record RoomJoinedResponse(Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
    private sealed record RoomSnapshot(Guid RoomId, string InviteCode, Guid HostPlayerId, int MaxPlayers, string Status, long Revision, IReadOnlyList<PlayerSnapshot> Players, RoomAiConfiguration? AiConfiguration);
    private sealed record PlayerSnapshot(Guid PlayerId, string Nickname, bool IsHost, bool IsReady, bool IsConnected);
}
