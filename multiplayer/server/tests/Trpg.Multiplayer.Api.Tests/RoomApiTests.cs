using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests;

public sealed class RoomApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Create_ReturnsSessionTokenAndSnapshotWithoutLeakingToken()
    {
        var response = await CreateAsync("Host", 2);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<RoomCreatedResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.RoomId);
        Assert.NotEqual(Guid.Empty, created.PlayerId);
        Assert.False(string.IsNullOrWhiteSpace(created.InviteCode));
        Assert.False(string.IsNullOrWhiteSpace(created.PlayerSessionToken));
        Assert.Equal(created.RoomId, created.Room.RoomId);
        Assert.Equal(created.InviteCode, created.Room.InviteCode);
        var snapshotJson = JsonSerializer.Serialize(created.Room);
        Assert.DoesNotContain("PlayerSessionToken", snapshotJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(created.PlayerSessionToken, snapshotJson);
    }

    [Fact]
    public async Task Join_ByInvite_ReturnsDistinctPlayerTokenAndSnapshot()
    {
        var created = await ReadCreatedAsync(await CreateAsync("Host", 2));
        var response = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = created.InviteCode, nickname = "Player" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var joined = await response.Content.ReadFromJsonAsync<RoomJoinedResponse>();
        Assert.NotNull(joined);
        Assert.NotEqual(created.PlayerId, joined.PlayerId);
        Assert.NotEqual(created.PlayerSessionToken, joined.PlayerSessionToken);
        Assert.Equal(2, joined.Room.Players.Count);
    }

    [Fact]
    public async Task Join_WhenRoomIsFull_ReturnsConflictWithoutIssuingToken()
    {
        var created = await ReadCreatedAsync(await CreateAsync("Host", 1));
        var response = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = created.InviteCode, nickname = "Player" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.DoesNotContain("playerSessionToken", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ready_UpdatesRevisionForAuthenticatedMember()
    {
        var created = await ReadCreatedAsync(await CreateAsync("Host", 2));
        var response = await PostAuthorizedAsync($"/api/rooms/{created.RoomId}/ready", created.PlayerSessionToken, new { isReady = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var room = await response.Content.ReadFromJsonAsync<RoomSnapshot>();
        Assert.NotNull(room);
        Assert.Equal(2, room.Revision);
        Assert.True(Assert.Single(room.Players).IsReady);
    }

    [Fact]
    public async Task ProtectedEndpoints_RejectMissingAndInvalidBearerTokens()
    {
        var created = await ReadCreatedAsync(await CreateAsync("Host", 2));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/rooms/{created.RoomId}/ready", new { isReady = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAuthorizedAsync($"/api/rooms/{created.RoomId}/leave", "not-a-valid-token", null)).StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoints_RejectTokenForAnotherRoom()
    {
        var first = await ReadCreatedAsync(await CreateAsync("First", 2));
        var second = await ReadCreatedAsync(await CreateAsync("Second", 2));

        var response = await PostAuthorizedAsync($"/api/rooms/{second.RoomId}/ready", first.PlayerSessionToken, new { isReady = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Leave_RemovesMemberAndHostLeaveClosesRoomAndInvalidatesInvite()
    {
        var host = await ReadCreatedAsync(await CreateAsync("Host", 2));
        var joinResponse = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = host.InviteCode, nickname = "Player" });
        var player = await joinResponse.Content.ReadFromJsonAsync<RoomJoinedResponse>();
        Assert.NotNull(player);

        var leaveMember = await PostAuthorizedAsync($"/api/rooms/{host.RoomId}/leave", player.PlayerSessionToken, null);
        Assert.Equal(HttpStatusCode.OK, leaveMember.StatusCode);

        var leaveHost = await PostAuthorizedAsync($"/api/rooms/{host.RoomId}/leave", host.PlayerSessionToken, null);
        Assert.Equal(HttpStatusCode.OK, leaveHost.StatusCode);

        var rejoin = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = host.InviteCode, nickname = "Late" });
        Assert.Equal(HttpStatusCode.NotFound, rejoin.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAuthorizedAsync($"/api/rooms/{host.RoomId}/leave", player.PlayerSessionToken, null)).StatusCode);
    }

    [Fact]
    public async Task HostLeave_RevokesAllRoomSessionTokens()
    {
        var host = await ReadCreatedAsync(await CreateAsync("Host", 2));
        var joinResponse = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = host.InviteCode, nickname = "Player" });
        var player = await joinResponse.Content.ReadFromJsonAsync<RoomJoinedResponse>();
        Assert.NotNull(player);

        var leaveHost = await PostAuthorizedAsync($"/api/rooms/{host.RoomId}/leave", host.PlayerSessionToken, null);
        Assert.Equal(HttpStatusCode.OK, leaveHost.StatusCode);

        var useRevokedToken = await PostAuthorizedAsync($"/api/rooms/{host.RoomId}/ready", player.PlayerSessionToken, new { isReady = true });
        Assert.Equal(HttpStatusCode.Unauthorized, useRevokedToken.StatusCode);
    }

    [Fact]
    public async Task Create_UsesNonEmptyUniqueInviteCodes()
    {
        var first = await ReadCreatedAsync(await CreateAsync("First", 2));
        var second = await ReadCreatedAsync(await CreateAsync("Second", 2));

        Assert.NotEqual(first.InviteCode, second.InviteCode);
    }

    private Task<HttpResponseMessage> CreateAsync(string nickname, int maxPlayers) =>
        factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname, maxPlayers });

    private async Task<RoomCreatedResponse> ReadCreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<RoomCreatedResponse>(await response.Content.ReadFromJsonAsync<RoomCreatedResponse>());
    }

    private async Task<HttpResponseMessage> PostAuthorizedAsync(string path, string token, object? body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private sealed record RoomCreatedResponse(Guid RoomId, string InviteCode, Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
    private sealed record RoomJoinedResponse(Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
    private sealed record RoomSnapshot(Guid RoomId, string InviteCode, Guid HostPlayerId, int MaxPlayers, string Status, long Revision, IReadOnlyList<PlayerSnapshot> Players);
    private sealed record PlayerSnapshot(Guid PlayerId, string Nickname, bool IsHost, bool IsReady, bool IsConnected);
}
