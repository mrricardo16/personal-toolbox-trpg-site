using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Realtime;
using Xunit;

namespace Trpg.Multiplayer.Api.Tests.Realtime;

public sealed class SignalRGameDeliveryTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private static readonly TimeSpan EventTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan IsolationTimeout = TimeSpan.FromSeconds(1);
    private readonly List<HubConnection> connections = [];

    [Fact]
    public async Task InitializeAndCheck_DeliverViewerSafeSnapshotsAndSemanticEventToEveryAttachedPlayer()
    {
        var room = await CreateRoomAsync("Host", 3);
        var member = await JoinRoomAsync(room.InviteCode, "Member");
        var hostConnection = await AttachAsync(room.PlayerSessionToken);
        var memberConnection = await AttachAsync(member.PlayerSessionToken);
        var hostSnapshots = NewCompletion<GameSnapshot>();
        var memberSnapshots = NewCompletion<GameSnapshot>();
        var hostCheckSnapshots = NewCompletion<GameSnapshot>();
        var memberCheckSnapshots = NewCompletion<GameSnapshot>();
        var hostChecks = NewCompletion<CheckResolvedEvent>();
        var memberChecks = NewCompletion<CheckResolvedEvent>();
        hostConnection.On<GameSnapshot>("GameSnapshot", snapshot =>
        {
            if (snapshot.Revision == 1) hostSnapshots.TrySetResult(snapshot);
            if (snapshot.Revision == 2) hostCheckSnapshots.TrySetResult(snapshot);
        });
        memberConnection.On<GameSnapshot>("GameSnapshot", snapshot =>
        {
            if (snapshot.Revision == 1) memberSnapshots.TrySetResult(snapshot);
            if (snapshot.Revision == 2) memberCheckSnapshots.TrySetResult(snapshot);
        });
        hostConnection.On<CheckResolvedEvent>("CheckResolved", message => hostChecks.TrySetResult(message));
        memberConnection.On<CheckResolvedEvent>("CheckResolved", message => memberChecks.TrySetResult(message));

        using var initialize = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/game/initialize",
            room.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = room.PlayerId, name = "Host Character", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } },
                    new { playerId = member.PlayerId, name = "Member Character", checkValues = new Dictionary<string, int> { ["spotHidden"] = 40 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });

        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        var hostSnapshot = await hostSnapshots.Task.WaitAsync(EventTimeout);
        var memberSnapshot = await memberSnapshots.Task.WaitAsync(EventTimeout);
        var hostCharacter = Assert.Single(hostSnapshot.Characters, character => character.OwnerPlayerId == room.PlayerId);
        var memberCharacter = Assert.Single(hostSnapshot.Characters, character => character.OwnerPlayerId == member.PlayerId);
        Assert.Equal(1, hostSnapshot.Revision);
        Assert.Equal(1, memberSnapshot.Revision);
        Assert.Equal(60, hostSnapshot.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).CheckValues["spotHidden"]);
        Assert.Empty(hostSnapshot.Characters.Single(character => character.OwnerPlayerId == member.PlayerId).CheckValues);
        Assert.Equal(40, memberSnapshot.Characters.Single(character => character.OwnerPlayerId == member.PlayerId).CheckValues["spotHidden"]);
        Assert.Empty(memberSnapshot.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).CheckValues);

        using var check = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/game/check",
            room.PlayerSessionToken,
            new { characterId = hostCharacter.CharacterId, checkKey = "spotHidden" });

        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
        var resolvedHostEvent = await hostChecks.Task.WaitAsync(EventTimeout);
        var resolvedMemberEvent = await memberChecks.Task.WaitAsync(EventTimeout);
        var resolvedHostSnapshot = await hostCheckSnapshots.Task.WaitAsync(EventTimeout);
        var resolvedMemberSnapshot = await memberCheckSnapshots.Task.WaitAsync(EventTimeout);
        Assert.Equal(room.RoomId, resolvedHostEvent.RoomId);
        Assert.Equal(resolvedHostEvent.CheckId, resolvedMemberEvent.CheckId);
        Assert.Equal(hostCharacter.CharacterId, resolvedHostEvent.CharacterId);
        Assert.Equal("spotHidden", resolvedHostEvent.CheckKey);
        Assert.Equal(2, resolvedHostEvent.GameRevision);
        Assert.Equal(2, resolvedHostSnapshot.Revision);
        Assert.Equal(2, resolvedMemberSnapshot.Revision);
        Assert.NotNull(resolvedMemberSnapshot.LastCheck);
        Assert.Equal(resolvedHostEvent.CheckId, resolvedMemberSnapshot.LastCheck!.CheckId);
        Assert.DoesNotContain("target", JsonSerializer.Serialize(resolvedHostEvent), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("roll", JsonSerializer.Serialize(resolvedHostEvent), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlayerSessionToken", JsonSerializer.Serialize(resolvedHostSnapshot), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PlayerSessionToken", JsonSerializer.Serialize(resolvedMemberSnapshot), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AttachAfterGameInitialization_RecoversGameSnapshotForOnlyThatPlayer()
    {
        var room = await CreateRoomAsync("Host", 2);
        using var initialize = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/game/initialize",
            room.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = room.PlayerId, name = "Investigator", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);

        var gameSnapshot = NewCompletion<GameSnapshot>();
        var connection = CreateHubConnection();
        connection.On<GameSnapshot>("GameSnapshot", snapshot => gameSnapshot.TrySetResult(snapshot));
        await connection.StartAsync();
        var roomSnapshot = await connection.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);
        var recovered = await gameSnapshot.Task.WaitAsync(EventTimeout);

        Assert.Equal(room.RoomId, roomSnapshot.RoomId);
        Assert.Equal(room.RoomId, recovered.RoomId);
        Assert.Equal(1, recovered.Revision);
        Assert.Equal("Investigator", Assert.Single(recovered.Characters).Name);
    }

    [Fact]
    public async Task InternalDamage_CommitsBeforePublishingViewerSafeSnapshotsAndReconnectRecoversHp()
    {
        var room = await CreateRoomAsync("Host", 2);
        var member = await JoinRoomAsync(room.InviteCode, "Member");
        var hostConnection = await AttachAsync(room.PlayerSessionToken);
        var memberConnection = await AttachAsync(member.PlayerSessionToken);
        using var initialize = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/game/initialize",
            room.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = room.PlayerId, name = "Host", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } },
                    new { playerId = member.PlayerId, name = "Member", checkValues = new Dictionary<string, int> { ["spotHidden"] = 40 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        var initialized = Assert.IsType<GameSnapshot>(await initialize.Content.ReadFromJsonAsync<GameSnapshot>());
        var hostUpdate = NewCompletion<GameSnapshot>();
        var memberUpdate = NewCompletion<GameSnapshot>();
        hostConnection.On<GameSnapshot>("GameSnapshot", snapshot => { if (snapshot.Revision == 2) hostUpdate.TrySetResult(snapshot); });
        memberConnection.On<GameSnapshot>("GameSnapshot", snapshot => { if (snapshot.Revision == 2) memberUpdate.TrySetResult(snapshot); });
        var hostCharacter = initialized.Characters.Single(character => character.OwnerPlayerId == room.PlayerId);

        var result = await factory.Services.GetRequiredService<IGameCoordinator>().ApplyDamageAsync(
            new ApplyDamageCommand(room.RoomId, hostCharacter.CharacterId, "trusted-test", 5));
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Snapshot.Revision);
        var hostSnapshot = await hostUpdate.Task.WaitAsync(EventTimeout);
        var memberSnapshot = await memberUpdate.Task.WaitAsync(EventTimeout);
        Assert.Equal(7, hostSnapshot.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).Health!.CurrentHp);
        Assert.Null(memberSnapshot.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).Health);

        await hostConnection.StopAsync();
        var reattachedSnapshot = NewCompletion<GameSnapshot>();
        var reattached = CreateHubConnection();
        reattached.On<GameSnapshot>("GameSnapshot", snapshot => reattachedSnapshot.TrySetResult(snapshot));
        await reattached.StartAsync();
        await reattached.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);
        var recovered = await reattachedSnapshot.Task.WaitAsync(EventTimeout);
        Assert.Equal(2, recovered.Revision);
        Assert.Equal(7, recovered.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).Health!.CurrentHp);
    }

    [Fact]
    public async Task GameDelivery_IsolatedAcrossRooms_AndReattachKeepsCanonicalGameState()
    {
        var first = await CreateRoomAsync("First Host", 2);
        var second = await CreateRoomAsync("Second Host", 2);
        var firstConnection = await AttachAsync(first.PlayerSessionToken);
        var secondConnection = await AttachAsync(second.PlayerSessionToken);
        var firstGameSnapshot = NewCompletion<GameSnapshot>();
        var secondRoomLeak = NewCompletion<GameSnapshot>();
        firstConnection.On<GameSnapshot>("GameSnapshot", snapshot => firstGameSnapshot.TrySetResult(snapshot));
        secondConnection.On<GameSnapshot>("GameSnapshot", snapshot => secondRoomLeak.TrySetResult(snapshot));

        using var initialize = await PostAuthorizedAsync(
            $"/api/rooms/{first.RoomId}/game/initialize",
            first.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = first.PlayerId, name = "First Character", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        Assert.Equal(first.RoomId, (await firstGameSnapshot.Task.WaitAsync(EventTimeout)).RoomId);
        await AssertNoEventWithinAsync(secondRoomLeak.Task);

        await firstConnection.StopAsync();
        var reattachedSnapshot = NewCompletion<GameSnapshot>();
        var reattached = CreateHubConnection();
        reattached.On<GameSnapshot>("GameSnapshot", snapshot => reattachedSnapshot.TrySetResult(snapshot));
        await reattached.StartAsync();
        await reattached.InvokeAsync<RoomSnapshot>("AttachSession", first.PlayerSessionToken);
        var recovered = await reattachedSnapshot.Task.WaitAsync(EventTimeout);

        Assert.Equal(first.RoomId, recovered.RoomId);
        Assert.Equal(1, recovered.Revision);
        Assert.True(factory.Services.GetRequiredService<IGameStateStore>().Exists(first.RoomId));
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private HubConnection CreateHubConnection()
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/room"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
            })
            .Build();
        connections.Add(connection);
        return connection;
    }

    private async Task<HubConnection> AttachAsync(string token)
    {
        var connection = CreateHubConnection();
        await connection.StartAsync();
        await connection.InvokeAsync<RoomSnapshot>("AttachSession", token);
        return connection;
    }

    private async Task<RoomCreatedResponse> CreateRoomAsync(string nickname, int maxPlayers)
    {
        using var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/rooms",
            new { nickname, maxPlayers });
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

    private async Task<HttpResponseMessage> PostAuthorizedAsync(string path, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await factory.CreateClient().SendAsync(request);
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task AssertNoEventWithinAsync<T>(Task<T> task)
    {
        try
        {
            await task.WaitAsync(IsolationTimeout);
            throw new Xunit.Sdk.XunitException("Unexpected cross-room game event was delivered.");
        }
        catch (TimeoutException)
        {
        }
    }

    private sealed record RoomCreatedResponse(
        Guid RoomId,
        string InviteCode,
        Guid PlayerId,
        string PlayerSessionToken);

    private sealed record RoomJoinedResponse(Guid PlayerId, string PlayerSessionToken);
}
