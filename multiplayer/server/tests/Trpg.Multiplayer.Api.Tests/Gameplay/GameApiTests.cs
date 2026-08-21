using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Trpg.Multiplayer.Api.Gameplay;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Gameplay;

public sealed class GameApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task InitializeAndGet_ReturnsPlayerSafeProjectionForAuthenticatedMember()
    {
        var created = await ReadCreatedAsync(await factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        var initialize = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/initialize",
            created.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = created.PlayerId, name = "Investigator", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });

        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        var snapshot = await initialize.Content.ReadFromJsonAsync<GameSnapshot>();
        Assert.NotNull(snapshot);
        Assert.Equal(created.RoomId, snapshot.RoomId);
        Assert.Equal(1, snapshot.Revision);
        Assert.Equal("Active", snapshot.Status);
        Assert.Equal("Investigator", Assert.Single(snapshot.Characters).Name);

        var get = await SendAuthorizedAsync(HttpMethod.Get, $"/api/rooms/{created.RoomId}/game", created.PlayerSessionToken, null);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(snapshot.Revision, (await get.Content.ReadFromJsonAsync<GameSnapshot>())!.Revision);
    }

    [Fact]
    public async Task GameEndpoints_RejectMissingAndCrossRoomSessions()
    {
        var first = await ReadCreatedAsync(await factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname = "First", maxPlayers = 2 }));
        var second = await ReadCreatedAsync(await factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname = "Second", maxPlayers = 2 }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/rooms/{first.RoomId}/game")).StatusCode);
        var crossRoom = await SendAuthorizedAsync(HttpMethod.Get, $"/api/rooms/{second.RoomId}/game", first.PlayerSessionToken, null);
        Assert.Equal(HttpStatusCode.Forbidden, crossRoom.StatusCode);
    }

    [Fact]
    public async Task Initialize_RejectsNonHostAndUnknownRosterMember()
    {
        var created = await ReadCreatedAsync(await factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        var joined = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = created.InviteCode, nickname = "Member" });
        var member = await joined.Content.ReadFromJsonAsync<JoinedResponse>();
        Assert.NotNull(member);

        var nonHost = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/initialize",
            member.PlayerSessionToken,
            new { characters = new[] { new { playerId = member.PlayerId, name = "Member", checkValues = new Dictionary<string, int> { ["spotHidden"] = 40 }, health = new { currentHp = 12, maxHp = 12, con = 60 } } } });
        Assert.Equal(HttpStatusCode.Forbidden, nonHost.StatusCode);

        var unknown = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/initialize",
            created.PlayerSessionToken,
            new { characters = new[] { new { playerId = Guid.NewGuid(), name = "Unknown", checkValues = new Dictionary<string, int> { ["spotHidden"] = 40 }, health = new { currentHp = 12, maxHp = 12, con = 60 } } } });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task HostClose_RemovesGameState()
    {
        var created = await ReadCreatedAsync(await factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        var initialize = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/initialize",
            created.PlayerSessionToken,
            new { characters = new[] { new { playerId = created.PlayerId, name = "Host", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } } } });
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        var gameStates = factory.Services.GetRequiredService<IGameStateStore>();
        Assert.True(gameStates.Exists(created.RoomId));

        var leave = await SendAuthorizedAsync(HttpMethod.Post, $"/api/rooms/{created.RoomId}/leave", created.PlayerSessionToken, null);
        Assert.Equal(HttpStatusCode.OK, leave.StatusCode);
        Assert.False(gameStates.Exists(created.RoomId));
    }

    [Fact]
    public async Task Check_UsesCanonicalCharacterTargetAndCommitsIndependentGameRevision()
    {
        var created = await ReadCreatedAsync(await factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        var initialized = await InitializeAsync(created, new[]
        {
            new { playerId = created.PlayerId, name = "Host", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
        });
        var game = await initialized.Content.ReadFromJsonAsync<GameSnapshot>();
        var characterId = Assert.Single(game!.Characters).CharacterId;

        var response = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/check",
            created.PlayerSessionToken,
            new { characterId, checkKey = "spotHidden", difficulty = "regular", target = 99, roll = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resolved = await response.Content.ReadFromJsonAsync<GameCheckResult>();
        Assert.NotNull(resolved);
        Assert.Equal(2, resolved.Snapshot.Revision);
        Assert.Equal(60, resolved.Check.Target);
        Assert.InRange(resolved.Check.Roll, 1, 100);

        var stateStore = factory.Services.GetRequiredService<IGameStateStore>();
        Assert.True(stateStore.TryGet(created.RoomId, out var state));
        Assert.Equal(2, state!.Revision);
        Assert.Equal(resolved.Check.Roll, state.LastCheck!.Roll);
        Assert.Equal(60, state.Characters.Single().CheckValues["spotHidden"]);

        var unknown = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/check",
            created.PlayerSessionToken,
            new { characterId, checkKey = "unknownSkill" });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        var afterFailure = await SendAuthorizedAsync(HttpMethod.Get, $"/api/rooms/{created.RoomId}/game", created.PlayerSessionToken, null);
        Assert.Equal(2, (await afterFailure.Content.ReadFromJsonAsync<GameSnapshot>())!.Revision);
    }

    [Fact]
    public async Task Check_RejectsCrossCharacterOwnershipAndGameRosterMutation()
    {
        var created = await ReadCreatedAsync(await factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 3 }));
        var memberResponse = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = created.InviteCode, nickname = "Member" });
        var member = Assert.IsType<JoinedResponse>(await memberResponse.Content.ReadFromJsonAsync<JoinedResponse>());
        var initialized = await InitializeAsync(created, new[]
        {
            new { playerId = created.PlayerId, name = "Host", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } },
            new { playerId = member.PlayerId, name = "Member", checkValues = new Dictionary<string, int> { ["spotHidden"] = 40 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
        });
        var game = await initialized.Content.ReadFromJsonAsync<GameSnapshot>();
        var hostCharacterId = game!.Characters.Single(character => character.OwnerPlayerId == created.PlayerId).CharacterId;

        var denied = await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/check",
            member.PlayerSessionToken,
            new { characterId = hostCharacterId, checkKey = "spotHidden" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var joinAfterStart = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = created.InviteCode, nickname = "Late" });
        Assert.Equal(HttpStatusCode.Conflict, joinAfterStart.StatusCode);
        var leaveAfterStart = await SendAuthorizedAsync(HttpMethod.Post, $"/api/rooms/{created.RoomId}/leave", member.PlayerSessionToken, null);
        Assert.Equal(HttpStatusCode.Conflict, leaveAfterStart.StatusCode);
    }

    [Fact]
    public async Task ConcurrentChecks_AreSerializedAndPreserveMonotonicGameRevisions()
    {
        var created = await ReadCreatedAsync(await factory.CreateClient().PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        var memberResponse = await factory.CreateClient().PostAsJsonAsync("/api/rooms/join", new { inviteCode = created.InviteCode, nickname = "Member" });
        var member = Assert.IsType<JoinedResponse>(await memberResponse.Content.ReadFromJsonAsync<JoinedResponse>());
        var initialized = await InitializeAsync(created, new[]
        {
            new { playerId = created.PlayerId, name = "Host", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } },
            new { playerId = member.PlayerId, name = "Member", checkValues = new Dictionary<string, int> { ["spotHidden"] = 40 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
        });
        var game = await initialized.Content.ReadFromJsonAsync<GameSnapshot>();
        var hostCharacterId = game!.Characters.Single(character => character.OwnerPlayerId == created.PlayerId).CharacterId;
        var memberCharacterId = game.Characters.Single(character => character.OwnerPlayerId == member.PlayerId).CharacterId;

        var checks = await Task.WhenAll(
            SendAuthorizedAsync(HttpMethod.Post, $"/api/rooms/{created.RoomId}/game/check", created.PlayerSessionToken, new { characterId = hostCharacterId, checkKey = "spotHidden" }),
            SendAuthorizedAsync(HttpMethod.Post, $"/api/rooms/{created.RoomId}/game/check", member.PlayerSessionToken, new { characterId = memberCharacterId, checkKey = "spotHidden" }));

        Assert.All(checks, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var projection = await SendAuthorizedAsync(HttpMethod.Get, $"/api/rooms/{created.RoomId}/game", created.PlayerSessionToken, null);
        Assert.Equal(3, (await projection.Content.ReadFromJsonAsync<GameSnapshot>())!.Revision);
        var stateStore = factory.Services.GetRequiredService<IGameStateStore>();
        Assert.True(stateStore.TryGet(created.RoomId, out var state));
        Assert.Equal(3, state!.Revision);
        Assert.NotNull(state.LastCheck);
    }

    private async Task<HttpResponseMessage> InitializeAsync(CreatedResponse created, object[] characters)
    {
        return await SendAuthorizedAsync(
            HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/initialize",
            created.PlayerSessionToken,
            new { characters });
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(HttpMethod method, string path, string token, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await factory.CreateClient().SendAsync(request);
    }

    private static async Task<CreatedResponse> ReadCreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<CreatedResponse>(await response.Content.ReadFromJsonAsync<CreatedResponse>());
    }

    private sealed record CreatedResponse(Guid RoomId, string InviteCode, Guid PlayerId, string PlayerSessionToken);

    private sealed record JoinedResponse(Guid PlayerId, string PlayerSessionToken);
}
