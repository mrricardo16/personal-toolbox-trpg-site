using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Trpg.Multiplayer.Api.Rooms;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests;

public sealed class RoomSnapshotApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetRoom_ReturnsSafeSnapshotForCurrentMember()
    {
        var room = await CreateRoomAsync("Host");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/rooms/{room.RoomId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", room.PlayerSessionToken);
        using var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<RoomSnapshot>();
        Assert.Equal(room.RoomId, snapshot!.RoomId);
        Assert.Equal(room.PlayerId, snapshot.HostPlayerId);
    }

    [Fact]
    public async Task GetRoom_RejectsMissingOrCrossRoomSession()
    {
        var first = await CreateRoomAsync("First");
        var second = await CreateRoomAsync("Second");

        using var missingToken = new HttpRequestMessage(HttpMethod.Get, $"/api/rooms/{first.RoomId}");
        using var missingResponse = await factory.CreateClient().SendAsync(missingToken);

        using var crossRoom = new HttpRequestMessage(HttpMethod.Get, $"/api/rooms/{second.RoomId}");
        crossRoom.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.PlayerSessionToken);
        using var crossRoomResponse = await factory.CreateClient().SendAsync(crossRoom);

        Assert.Equal(HttpStatusCode.Unauthorized, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossRoomResponse.StatusCode);
    }

    private async Task<RoomCreatedResponse> CreateRoomAsync(string nickname)
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/rooms",
            new { nickname, maxPlayers = 4 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<RoomCreatedResponse>(await response.Content.ReadFromJsonAsync<RoomCreatedResponse>());
    }

    private sealed record RoomCreatedResponse(Guid RoomId, string InviteCode, Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
    private sealed record RoomSnapshot(Guid RoomId, string InviteCode, Guid HostPlayerId, int MaxPlayers, string Status, long Revision, IReadOnlyList<PlayerSnapshot> Players, RoomAiConfiguration? AiConfiguration);
    private sealed record PlayerSnapshot(Guid PlayerId, string Nickname, bool IsHost, bool IsReady, bool IsConnected);
}
