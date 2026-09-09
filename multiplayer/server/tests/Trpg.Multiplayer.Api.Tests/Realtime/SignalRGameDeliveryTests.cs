using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public async Task InternalStabilization_DeliversOwnerStatusOnlyAfterCommitAndReconnectRecoversIt()
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
                    new { playerId = room.PlayerId, name = "Host", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 6, maxHp = 12, con = 60 } },
                    new { playerId = member.PlayerId, name = "Member", checkValues = new Dictionary<string, int> { ["spotHidden"] = 40 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        var initialized = Assert.IsType<GameSnapshot>(await initialize.Content.ReadFromJsonAsync<GameSnapshot>());
        var hostCharacter = initialized.Characters.Single(character => character.OwnerPlayerId == room.PlayerId);
        var hostUpdate = NewCompletion<GameSnapshot>();
        var memberUpdate = NewCompletion<GameSnapshot>();
        hostConnection.On<GameSnapshot>("GameSnapshot", snapshot => { if (snapshot.Revision == 3) hostUpdate.TrySetResult(snapshot); });
        memberConnection.On<GameSnapshot>("GameSnapshot", snapshot => { if (snapshot.Revision == 3) memberUpdate.TrySetResult(snapshot); });

        var coordinator = factory.Services.GetRequiredService<IGameCoordinator>();
        var damage = await coordinator.ApplyDamageAsync(new ApplyDamageCommand(
            room.RoomId,
            hostCharacter.CharacterId,
            "stabilization-damage",
            6,
            1));
        Assert.True(damage.IsSuccess);
        var stabilization = await coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(
            room.RoomId,
            hostCharacter.CharacterId,
            100,
            true,
            "stabilization-aid"));
        Assert.True(stabilization.IsSuccess);
        Assert.Equal(3, stabilization.Value!.Snapshot.Revision);

        var hostSnapshot = await hostUpdate.Task.WaitAsync(EventTimeout);
        var memberSnapshot = await memberUpdate.Task.WaitAsync(EventTimeout);
        var hostHealth = hostSnapshot.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).Health;
        var memberViewOfHost = memberSnapshot.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).Health;
        Assert.Equal(3, hostSnapshot.Revision);
        Assert.Equal(1, hostHealth!.CurrentHp);
        Assert.True(hostHealth.Stabilized);
        Assert.False(hostHealth.Dying);
        Assert.False(hostHealth.Dead);
        Assert.False(hostHealth.Unconscious);
        Assert.Null(memberViewOfHost);
        Assert.Equal(3, factory.Services.GetRequiredService<IGameStateStore>().TryGet(room.RoomId, out var committedState) ? committedState!.Revision : 0);

        var serializedHost = JsonSerializer.Serialize(hostSnapshot);
        Assert.DoesNotContain("DyingCheck", serializedHost, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Treatment", serializedHost, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SourceId", serializedHost, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reason", serializedHost, StringComparison.OrdinalIgnoreCase);

        await hostConnection.StopAsync();
        var reattachedSnapshot = NewCompletion<GameSnapshot>();
        var reattached = CreateHubConnection();
        reattached.On<GameSnapshot>("GameSnapshot", snapshot => reattachedSnapshot.TrySetResult(snapshot));
        await reattached.StartAsync();
        await reattached.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);
        var recovered = await reattachedSnapshot.Task.WaitAsync(EventTimeout);
        Assert.Equal(3, recovered.Revision);
        Assert.True(recovered.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).Health!.Stabilized);
    }

    [Fact]
    public async Task InternalStabilization_DoesNotPublishAcrossRooms()
    {
        var first = await CreateRoomAsync("First Host", 1);
        var second = await CreateRoomAsync("Second Host", 1);
        var firstConnection = await AttachAsync(first.PlayerSessionToken);
        var secondConnection = await AttachAsync(second.PlayerSessionToken);
        using var firstInitialize = await PostAuthorizedAsync(
            $"/api/rooms/{first.RoomId}/game/initialize",
            first.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = first.PlayerId, name = "First", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 6, maxHp = 12, con = 60 } }
                }
            });
        using var secondInitialize = await PostAuthorizedAsync(
            $"/api/rooms/{second.RoomId}/game/initialize",
            second.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = second.PlayerId, name = "Second", checkValues = new Dictionary<string, int> { ["spotHidden"] = 60 }, health = new { currentHp = 6, maxHp = 12, con = 60 } }
                }
            });
        var firstGame = Assert.IsType<GameSnapshot>(await firstInitialize.Content.ReadFromJsonAsync<GameSnapshot>());
        var firstCharacter = firstGame.Characters.Single().CharacterId;
        var secondLeak = NewCompletion<GameSnapshot>();
        secondConnection.On<GameSnapshot>("GameSnapshot", snapshot => { if (snapshot.Revision > 1) secondLeak.TrySetResult(snapshot); });

        var coordinator = factory.Services.GetRequiredService<IGameCoordinator>();
        Assert.True((await coordinator.ApplyDamageAsync(new ApplyDamageCommand(first.RoomId, firstCharacter, "room-one-damage", 6, 1))).IsSuccess);
        Assert.True((await coordinator.ResolveFirstAidAsync(new ResolveFirstAidCommand(first.RoomId, firstCharacter, 100, true, "room-one-aid"))).IsSuccess);

        await AssertNoEventWithinAsync(secondLeak.Task);
        Assert.True(factory.Services.GetRequiredService<IGameStateStore>().TryGet(second.RoomId, out var secondState));
        Assert.Equal(1, secondState!.Revision);
        _ = firstConnection;
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

    [Fact]
    public async Task InternalCombat_PublishesSafeSnapshotsAndAttachRecoversPendingWithoutMutation()
    {
        var room = await CreateRoomAsync("Host", 2);
        var member = await JoinRoomAsync(room.InviteCode, "Member");
        var hostConnection = await AttachAsync(room.PlayerSessionToken);
        var memberConnection = await AttachAsync(member.PlayerSessionToken);
        using var initialize = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/game/initialize", room.PlayerSessionToken, new
            {
                characters = new[]
                {
                    new { playerId = room.PlayerId, name = "Host", checkValues = new Dictionary<string, int> { ["dex"] = 80, ["fighting_brawl"] = 55, ["dodge"] = 45, ["str"] = 60, ["siz"] = 50 }, health = new { currentHp = 12, maxHp = 12, con = 60 } },
                    new { playerId = member.PlayerId, name = "Member", checkValues = new Dictionary<string, int> { ["dex"] = 70, ["fighting_brawl"] = 50, ["dodge"] = 40, ["str"] = 60, ["siz"] = 50 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });
        var initialized = Assert.IsType<GameSnapshot>(await initialize.Content.ReadFromJsonAsync<GameSnapshot>());
        var hostUpdate = NewCompletion<GameSnapshot>();
        var memberUpdate = NewCompletion<GameSnapshot>();
        hostConnection.On<GameSnapshot>("GameSnapshot", snapshot => { if (snapshot.Revision == 3) hostUpdate.TrySetResult(snapshot); });
        memberConnection.On<GameSnapshot>("GameSnapshot", snapshot => { if (snapshot.Revision == 3) memberUpdate.TrySetResult(snapshot); });

        var coordinator = Assert.IsType<GameCoordinator>(factory.Services.GetRequiredService<IGameCoordinator>());
        await InvokeInternalCombatAsync(coordinator, "StartCombatAsync", room.RoomId, room.PlayerId, 1L,
            new[] { initialized.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).CharacterId }, CreateOpponentDefinitions(coordinator));
        await InvokeInternalCombatAsync(coordinator, "BeginOpposedExchangeAsync", room.RoomId, room.PlayerId, 2L,
            "character:" + initialized.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).CharacterId, "opponent:0");

        var hostSnapshot = await hostUpdate.Task.WaitAsync(EventTimeout);
        var memberSnapshot = await memberUpdate.Task.WaitAsync(EventTimeout);
        Assert.True(factory.Services.GetRequiredService<IGameStateStore>().TryGet(room.RoomId, out var committed));
        var committedCombat = Assert.IsType<CombatSession>(committed!.Combat);
        var pendingExchangeId = committedCombat.PendingExchange!.ExchangeId;
        var currentActor = committedCombat.Order[committedCombat.TurnIndex].Value;
        var actionCounts = committedCombat.ActionCounts.ToDictionary(pair => pair.Key, pair => pair.Value);
        var responseCounts = committedCombat.ResponseCounts.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(3, committed.Revision);
        Assert.Equal(committed.Revision, hostSnapshot.Revision);
        Assert.NotNull(hostSnapshot.Combat);
        Assert.NotNull(hostSnapshot.Combat!.Pending);
        Assert.Null(memberSnapshot.Combat);
        Assert.Null(hostSnapshot.Combat.Participants.Single(participant => participant.CharacterId is null).Stats);
        var serializedSnapshot = JsonSerializer.Serialize(hostSnapshot);
        foreach (var internalTerm in new[] { "PendingDamageDispositions", "DyingSchedule", "AttackerCheck", "DefenderCheck", "Roll", "Target", "ResponsePolicy", "ResponseAllowance", "History", "SourceId", "Provenance" })
        {
            Assert.DoesNotContain(internalTerm, serializedSnapshot, StringComparison.OrdinalIgnoreCase);
        }

        await hostConnection.StopAsync();
        var recoveredEvent = NewCompletion<GameSnapshot>();
        var reattached = CreateHubConnection();
        reattached.On<GameSnapshot>("GameSnapshot", snapshot => recoveredEvent.TrySetResult(snapshot));
        await reattached.StartAsync();
        await reattached.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);
        var recovered = await recoveredEvent.Task.WaitAsync(EventTimeout);
        Assert.Equal(3, recovered.Revision);
        Assert.NotNull(recovered.Combat?.Pending);
        Assert.True(factory.Services.GetRequiredService<IGameStateStore>().TryGet(room.RoomId, out var canonical));
        Assert.Equal(3, canonical!.Revision);
        var canonicalCombat = Assert.IsType<CombatSession>(canonical.Combat);
        Assert.Equal(1, canonicalCombat.Round);
        Assert.Equal(currentActor, canonicalCombat.Order[canonicalCombat.TurnIndex].Value);
        Assert.Equal(pendingExchangeId, canonicalCombat.PendingExchange!.ExchangeId);
        Assert.Null(canonicalCombat.LastExchange);
        Assert.Equal(actionCounts, canonicalCombat.ActionCounts);
        Assert.Equal(responseCounts, canonicalCombat.ResponseCounts);
        Assert.Equal(JsonSerializer.Serialize(GameProjection.Build(canonical, room.PlayerId)), JsonSerializer.Serialize(recovered));
        await reattached.StopAsync();
    }

    [Fact]
    public async Task InternalCombatDamage_CommitsBeforeOneViewerSnapshot_ReplayAndCrossRoomStaySilent_AndReconnectRecovers()
    {
        var room = await CreateRoomAsync("Host", 4);
        var member = await JoinRoomAsync(room.InviteCode, "Member");
        var observer = await JoinRoomAsync(room.InviteCode, "Observer");
        var otherRoom = await CreateRoomAsync("Other Host", 1);
        using var initialize = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/game/initialize", room.PlayerSessionToken, new
            {
                characters = new[]
                {
                    new { playerId = room.PlayerId, name = "Host", checkValues = new Dictionary<string, int> { ["dex"] = 80, ["fighting_brawl"] = 55, ["dodge"] = 45, ["str"] = 60, ["siz"] = 50 }, health = new { currentHp = 12, maxHp = 12, con = 60 } },
                    new { playerId = member.PlayerId, name = "Member", checkValues = new Dictionary<string, int> { ["dex"] = 75, ["fighting_brawl"] = 50, ["dodge"] = 40, ["str"] = 60, ["siz"] = 50 }, health = new { currentHp = 12, maxHp = 12, con = 60 } },
                    new { playerId = observer.PlayerId, name = "Observer", checkValues = new Dictionary<string, int> { ["dex"] = 65, ["fighting_brawl"] = 45, ["dodge"] = 35, ["str"] = 60, ["siz"] = 50 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            });
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        var initialized = Assert.IsType<GameSnapshot>(await initialize.Content.ReadFromJsonAsync<GameSnapshot>());
        var hostCharacterId = initialized.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).CharacterId;
        var memberCharacterId = initialized.Characters.Single(character => character.OwnerPlayerId == member.PlayerId).CharacterId;
        var coordinator = Assert.IsType<GameCoordinator>(factory.Services.GetRequiredService<IGameCoordinator>());
        await InvokeInternalCombatAsync(
            coordinator,
            "StartCombatAsync",
            room.RoomId,
            room.PlayerId,
            1L,
            new[] { hostCharacterId, memberCharacterId },
            CreateOpponentDefinitions(coordinator));
        var stateStore = factory.Services.GetRequiredService<IGameStateStore>();
        var seeded = SeedPendingDamageDisposition(
            stateStore,
            room.RoomId,
            "opponent:0",
            "character:" + hostCharacterId);
        var expectedRevision = seeded.State.Revision + 1;

        var disconnectedHost = await AttachAsync(room.PlayerSessionToken);
        await disconnectedHost.StopAsync();
        var memberConnection = await AttachAsync(member.PlayerSessionToken);
        var observerConnection = await AttachAsync(observer.PlayerSessionToken);
        var otherRoomConnection = await AttachAsync(otherRoom.PlayerSessionToken);
        var memberUpdate = NewCompletion<(GameSnapshot Snapshot, bool Committed)>();
        var observerUpdate = NewCompletion<(GameSnapshot Snapshot, bool Committed)>();
        var duplicateDelivery = NewCompletion<GameSnapshot>();
        var crossRoomLeak = NewCompletion<GameSnapshot>();
        var memberDeliveryCount = 0;
        var observerDeliveryCount = 0;
        memberConnection.On<GameSnapshot>("GameSnapshot", snapshot =>
        {
            if (snapshot.Revision != expectedRevision)
            {
                return;
            }

            var deliveryNumber = Interlocked.Increment(ref memberDeliveryCount);
            var committed = stateStore.TryGet(room.RoomId, out var state)
                && state?.Revision == snapshot.Revision
                && state.Combat!.DamageDispositions[seeded.ExchangeId].Status == DamageDispositionStatus.Consumed;
            if (deliveryNumber == 1)
            {
                memberUpdate.TrySetResult((snapshot, committed));
            }
            else
            {
                duplicateDelivery.TrySetResult(snapshot);
            }
        });
        observerConnection.On<GameSnapshot>("GameSnapshot", snapshot =>
        {
            if (snapshot.Revision != expectedRevision)
            {
                return;
            }

            var deliveryNumber = Interlocked.Increment(ref observerDeliveryCount);
            var committed = stateStore.TryGet(room.RoomId, out var state)
                && state?.Revision == snapshot.Revision
                && state.Combat!.DamageDispositions[seeded.ExchangeId].Status == DamageDispositionStatus.Consumed;
            if (deliveryNumber == 1)
            {
                observerUpdate.TrySetResult((snapshot, committed));
            }
            else
            {
                duplicateDelivery.TrySetResult(snapshot);
            }
        });
        otherRoomConnection.On<GameSnapshot>("GameSnapshot", snapshot => crossRoomLeak.TrySetResult(snapshot));

        var consumed = await InvokeInternalCombatAsync(
            coordinator,
            "ResolveCombatDamageAsync",
            room.RoomId,
            seeded.State.Revision,
            seeded.ExchangeId);
        Assert.True(ReadBooleanProperty(consumed, "Changed"));
        var memberObserved = await memberUpdate.Task.WaitAsync(EventTimeout);
        var observerObserved = await observerUpdate.Task.WaitAsync(EventTimeout);
        Assert.True(memberObserved.Committed);
        Assert.True(observerObserved.Committed);
        Assert.Equal(1, Volatile.Read(ref memberDeliveryCount));
        Assert.Equal(1, Volatile.Read(ref observerDeliveryCount));
        Assert.Null(memberObserved.Snapshot.Characters.Single(character => character.CharacterId == hostCharacterId).Health);
        Assert.Equal(12, memberObserved.Snapshot.Characters.Single(character => character.CharacterId == memberCharacterId).Health!.CurrentHp);
        Assert.False(memberObserved.Snapshot.Combat!.LastExchange!.DispositionPending);
        Assert.Equal(seeded.ExchangeId, memberObserved.Snapshot.Combat.LastDamage!.ExchangeId);
        Assert.True(memberObserved.Snapshot.Combat!.LastDamage!.TargetDefeated);
        Assert.Equal("character:" + memberCharacterId, memberObserved.Snapshot.Combat.CurrentActorParticipantId);
        Assert.Null(observerObserved.Snapshot.Combat);

        var replay = await InvokeInternalCombatAsync(
            coordinator,
            "ResolveCombatDamageAsync",
            room.RoomId,
            seeded.State.Revision,
            seeded.ExchangeId);
        Assert.False(ReadBooleanProperty(replay, "Changed"));
        await AssertNoEventWithinAsync(duplicateDelivery.Task);
        await AssertNoEventWithinAsync(crossRoomLeak.Task);

        Assert.True(stateStore.TryGet(room.RoomId, out var beforeAttach));
        var retainedResult = beforeAttach!.Combat!.DamageDispositions[seeded.ExchangeId].Result;
        var reattachedEvent = NewCompletion<GameSnapshot>();
        var reattached = CreateHubConnection();
        reattached.On<GameSnapshot>("GameSnapshot", snapshot => reattachedEvent.TrySetResult(snapshot));
        await reattached.StartAsync();
        await reattached.InvokeAsync<RoomSnapshot>("AttachSession", room.PlayerSessionToken);
        var recovered = await reattachedEvent.Task.WaitAsync(EventTimeout);
        Assert.True(stateStore.TryGet(room.RoomId, out var afterAttach));
        Assert.Same(beforeAttach, afterAttach);
        Assert.Same(retainedResult, afterAttach!.Combat!.DamageDispositions[seeded.ExchangeId].Result);
        Assert.Equal(expectedRevision, recovered.Revision);
        var recoveredHealth = recovered.Characters.Single(character => character.CharacterId == hostCharacterId).Health;
        Assert.Equal(0, recoveredHealth!.CurrentHp);
        Assert.True(recoveredHealth.Dead);
        Assert.True(recovered.Combat!.LastDamage!.TargetDefeated);
        Assert.Equal(seeded.ExchangeId, recovered.Combat.LastDamage.ExchangeId);
        Assert.Equal("character:" + memberCharacterId, recovered.Combat.CurrentActorParticipantId);
        Assert.Equal(afterAttach.Combat.Order.Select(participant => participant.Value), recovered.Combat.Participants.Select(participant => participant.ParticipantId));
        var recoveredJson = JsonSerializer.Serialize(recovered);
        Assert.DoesNotContain("WeaponResult", recoveredJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DamageBonusResult", recoveredJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EventKey", recoveredJson, StringComparison.OrdinalIgnoreCase);
        await reattached.StopAsync();
    }

    [Fact]
    public async Task PlayerMeleeAttack_NpcDamage_PublishesBeginResolveDamageOnlyAfterEachCommitInRevisionOrder()
    {
        var delivery = await RunNpcMeleeAttackAsync([1, 100], [2]);

        Assert.Equal([3L, 4L, 5L], delivery.Actor.Select(item => item.Snapshot.Revision));
        Assert.All(delivery.Actor, item => Assert.True(item.Committed));
        Assert.All(delivery.Actor, item => Assert.True(item.Snapshot.Revision > delivery.RequestRevision));
        Assert.Equal(3, delivery.Actor.Select(item => item.Snapshot.Revision).Distinct().Count());
        Assert.Equal(delivery.HttpSnapshot.Revision, delivery.Actor[^1].Snapshot.Revision);
    }

    [Fact]
    public async Task PlayerMeleeAttack_NpcNoDamage_PublishesBeginAndResolveExactlyOnce()
    {
        var delivery = await RunNpcMeleeAttackAsync([100, 1], []);

        Assert.Equal([3L, 4L], delivery.Actor.Select(item => item.Snapshot.Revision));
        Assert.All(delivery.Actor, item => Assert.True(item.Committed));
        Assert.Equal(4, delivery.HttpSnapshot.Revision);
        Assert.Null(delivery.HttpSnapshot.Combat!.LastDamage);
    }

    [Fact]
    public async Task PlayerMeleeAttack_HumanDefender_PublishesBeginOnlyWithViewerSpecificAffordances()
    {
        var delivery = await RunHumanDefenderMeleeAttackAsync();

        var actor = Assert.Single(delivery.Actor);
        var defender = Assert.Single(delivery.Defender);
        var observer = Assert.Single(delivery.Nonparticipant);
        Assert.True(actor.Committed);
        Assert.True(defender.Committed);
        Assert.True(observer.Committed);
        Assert.Equal(3, actor.Snapshot.Revision);
        Assert.Null(actor.Snapshot.Combat!.ViewerActions?.PendingResponse);
        var pendingResponse = Assert.IsType<CombatPendingResponseSnapshot>(
            defender.Snapshot.Combat!.ViewerActions!.PendingResponse);
        Assert.Equal(delivery.ExchangeId, pendingResponse.ExchangeId);
        Assert.Equal(["dodge", "fight_back"], pendingResponse.AvailableResponses);
        Assert.Null(observer.Snapshot.Combat);
        Assert.Equal(3, delivery.HttpSnapshot.Revision);
        AssertViewerSafeParticipantStats(actor.Snapshot);
        AssertViewerSafeParticipantStats(defender.Snapshot);
    }

    [Fact]
    public async Task PlayerCombatIntent_NonparticipantReceivesNullCombatAtEveryPublishedRevision()
    {
        var delivery = await RunNpcMeleeAttackAsync([1, 100], [2]);

        Assert.Equal([3L, 4L, 5L], delivery.Nonparticipant.Select(item => item.Snapshot.Revision));
        Assert.All(delivery.Nonparticipant, item => Assert.Null(item.Snapshot.Combat));
    }

    [Fact]
    public async Task PlayerCombatIntent_RealtimeJsonNeverContainsPolicyAllowanceRollsStatsRegistryScheduleHistorySourceOrProvenance()
    {
        var npcDelivery = await RunNpcMeleeAttackAsync([1, 100], [2]);
        var humanDelivery = await RunHumanDefenderMeleeAttackAsync();
        var snapshots = npcDelivery.Actor.Select(item => item.Snapshot)
            .Concat(npcDelivery.Nonparticipant.Select(item => item.Snapshot))
            .Concat(humanDelivery.Actor.Select(item => item.Snapshot))
            .Concat(humanDelivery.Defender.Select(item => item.Snapshot))
            .Concat(humanDelivery.Nonparticipant.Select(item => item.Snapshot));

        foreach (var snapshot in snapshots)
        {
            AssertViewerSafeParticipantStats(snapshot);
            var json = JsonSerializer.Serialize(snapshot);
            foreach (var forbidden in new[]
            {
                "NpcResponsePolicy", "ResponseAllowance", "AttackerCheck", "DefenderCheck",
                "RawRolls", "DamageDispositions", "DyingSchedule", "History", "SourceId", "Provenance"
            })
            {
                Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task PlayerCombatIntent_ApplicationCoordinatorAddsNoDuplicatePublish()
    {
        var delivery = await RunNpcMeleeAttackAsync([1, 100], [2]);

        Assert.Equal(3, delivery.Actor.Count);
        Assert.Equal(3, delivery.Nonparticipant.Count);
        Assert.Equal(3, delivery.Actor.Select(item => item.Snapshot.Revision).Distinct().Count());
        Assert.Equal(3, delivery.Nonparticipant.Select(item => item.Snapshot.Revision).Distinct().Count());
        Assert.Equal([3L, 4L, 5L], delivery.PublishedRevisions);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in connections)
        {
            await connection.DisposeAsync();
        }
    }

    private HubConnection CreateHubConnection(WebApplicationFactory<Program>? app = null)
    {
        var server = (app ?? factory).Server;
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

    private async Task<HubConnection> AttachAsync(string token, WebApplicationFactory<Program>? app = null)
    {
        var connection = CreateHubConnection(app);
        await connection.StartAsync();
        await connection.InvokeAsync<RoomSnapshot>("AttachSession", token);
        return connection;
    }

    private async Task<CombatDelivery> RunNpcMeleeAttackAsync(
        IReadOnlyList<int> percentileRolls,
        IReadOnlyList<int> genericRolls)
    {
        using var isolatedFactory = CreateDeterministicFactory(percentileRolls, genericRolls);
        var setup = await CreatePlayerCombatSetupAsync(isolatedFactory, humanDefender: false);
        return await ExecuteMeleeAttackAsync(isolatedFactory, setup, expectedPublishedCount: genericRolls.Count > 0 ? 3 : 2);
    }

    private async Task<CombatDelivery> RunHumanDefenderMeleeAttackAsync()
    {
        using var isolatedFactory = CreateDeterministicFactory([], []);
        var setup = await CreatePlayerCombatSetupAsync(isolatedFactory, humanDefender: true);
        return await ExecuteMeleeAttackAsync(isolatedFactory, setup, expectedPublishedCount: 1);
    }

    private WebApplicationFactory<Program> CreateDeterministicFactory(
        IReadOnlyList<int> percentileRolls,
        IReadOnlyList<int> genericRolls) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDiceRoller>();
            services.AddSingleton<IDiceRoller>(new SequenceDiceRoller(percentileRolls, genericRolls));
            services.RemoveAll<IGameRealtimeNotifier>();
            services.AddSingleton<CommitTrackingGameRealtimeNotifier>();
            services.AddSingleton<IGameRealtimeNotifier>(provider =>
                provider.GetRequiredService<CommitTrackingGameRealtimeNotifier>());
        }));

    private async Task<PlayerCombatSetup> CreatePlayerCombatSetupAsync(
        WebApplicationFactory<Program> app,
        bool humanDefender)
    {
        var room = await CreateRoomAsync("Actor", 4, app);
        var defender = await JoinRoomAsync(room.InviteCode, "Defender", app);
        var observer = await JoinRoomAsync(room.InviteCode, "Observer", app);
        using var initialize = await PostAuthorizedAsync(
            $"/api/rooms/{room.RoomId}/game/initialize",
            room.PlayerSessionToken,
            new
            {
                characters = new[]
                {
                    new { playerId = room.PlayerId, name = "Actor", checkValues = new Dictionary<string, int> { ["dex"] = 80, ["fighting_brawl"] = 80, ["dodge"] = 40, ["str"] = 60, ["siz"] = 50 }, health = new { currentHp = 12, maxHp = 12, con = 60 } },
                    new { playerId = defender.PlayerId, name = "Defender", checkValues = new Dictionary<string, int> { ["dex"] = 70, ["fighting_brawl"] = 50, ["dodge"] = 60, ["str"] = 50, ["siz"] = 50 }, health = new { currentHp = 12, maxHp = 12, con = 60 } }
                }
            },
            app);
        Assert.Equal(HttpStatusCode.Created, initialize.StatusCode);
        var initialized = Assert.IsType<GameSnapshot>(await initialize.Content.ReadFromJsonAsync<GameSnapshot>());
        var actorCharacterId = initialized.Characters.Single(character => character.OwnerPlayerId == room.PlayerId).CharacterId;
        var defenderCharacterId = initialized.Characters.Single(character => character.OwnerPlayerId == defender.PlayerId).CharacterId;
        var coordinator = Assert.IsType<GameCoordinator>(app.Services.GetRequiredService<IGameCoordinator>());
        var started = await InvokeInternalCombatAsync(
            coordinator,
            "StartCombatAsync",
            room.RoomId,
            room.PlayerId,
            1L,
            new[] { actorCharacterId },
            CreateOpponentDefinitions(coordinator));
        var startValue = GetProperty<object>(started, "Value")!;
        var startedState = GetProperty<MultiplayerGameState>(startValue, "State")!;

        if (humanDefender)
        {
            var store = app.Services.GetRequiredService<IGameStateStore>();
            var participants = startedState!.Combat!.Participants
                .Select(participant => participant.ParticipantId.Value == "opponent:0"
                    ? participant with
                    {
                        CharacterId = defenderCharacterId,
                        OwnerPlayerId = defender.PlayerId,
                        Kind = "investigator"
                    }
                    : participant)
                .ToArray();
            var replacement = new MultiplayerGameState(
                startedState.RoomId,
                startedState.Revision,
                startedState.Status,
                startedState.CreatedAt,
                startedState.Characters,
                startedState.LastCheck,
                startedState.Combat with { Participants = participants });
            Assert.True(store.TryReplace(startedState, replacement));
        }

        return new PlayerCombatSetup(room, defender, observer, actorCharacterId);
    }

    private async Task<CombatDelivery> ExecuteMeleeAttackAsync(
        WebApplicationFactory<Program> app,
        PlayerCombatSetup setup,
        int expectedPublishedCount)
    {
        var store = app.Services.GetRequiredService<IGameStateStore>();
        var publicationTracker = app.Services.GetRequiredService<CommitTrackingGameRealtimeNotifier>();
        var publicationBaseline = publicationTracker.GetPublishedRevisions().Count;
        var actorConnection = await AttachAsync(setup.Room.PlayerSessionToken, app);
        var defenderConnection = await AttachAsync(setup.Defender.PlayerSessionToken, app);
        var observerConnection = await AttachAsync(setup.Observer.PlayerSessionToken, app);
        var actor = new ConcurrentQueue<ObservedSnapshot>();
        var defender = new ConcurrentQueue<ObservedSnapshot>();
        var nonparticipant = new ConcurrentQueue<ObservedSnapshot>();
        var actorComplete = NewCompletion();
        var defenderComplete = NewCompletion();
        var observerComplete = NewCompletion();

        Register(actorConnection, actor, actorComplete);
        Register(defenderConnection, defender, defenderComplete);
        Register(observerConnection, nonparticipant, observerComplete);

        using var response = await PostAuthorizedAsync(
            $"/api/rooms/{setup.Room.RoomId}/game/combat/melee-attack",
            setup.Room.PlayerSessionToken,
            new
            {
                expectedGameRevision = 2,
                actorCharacterId = setup.ActorCharacterId,
                targetParticipantId = "opponent:0"
            },
            app);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var httpSnapshot = Assert.IsType<GameSnapshot>(await response.Content.ReadFromJsonAsync<GameSnapshot>());
        await Task.WhenAll(
            actorComplete.Task.WaitAsync(EventTimeout),
            defenderComplete.Task.WaitAsync(EventTimeout),
            observerComplete.Task.WaitAsync(EventTimeout));

        return new CombatDelivery(
            2,
            actor.ToArray(),
            defender.ToArray(),
            nonparticipant.ToArray(),
            httpSnapshot,
            store.TryGet(setup.Room.RoomId, out var state)
                ? state!.Combat!.PendingExchange?.ExchangeId
                : null,
            publicationTracker.GetPublishedRevisions().Skip(publicationBaseline).ToArray());

        void Register(
            HubConnection connection,
            ConcurrentQueue<ObservedSnapshot> snapshots,
            TaskCompletionSource completion)
        {
            connection.On<GameSnapshot>("GameSnapshot", snapshot =>
            {
                var committed = publicationTracker.WasCommittedBeforePublish(snapshot.Revision);
                snapshots.Enqueue(new ObservedSnapshot(snapshot, committed));
                if (snapshots.Count >= expectedPublishedCount)
                {
                    completion.TrySetResult();
                }
            });
        }
    }

    private async Task<RoomCreatedResponse> CreateRoomAsync(
        string nickname,
        int maxPlayers,
        WebApplicationFactory<Program>? app = null)
    {
        using var response = await (app ?? factory).CreateClient().PostAsJsonAsync(
            "/api/rooms",
            new { nickname, maxPlayers });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<RoomCreatedResponse>(await response.Content.ReadFromJsonAsync<RoomCreatedResponse>());
    }

    private async Task<RoomJoinedResponse> JoinRoomAsync(
        string inviteCode,
        string nickname,
        WebApplicationFactory<Program>? app = null)
    {
        using var response = await (app ?? factory).CreateClient().PostAsJsonAsync(
            "/api/rooms/join",
            new { inviteCode, nickname });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<RoomJoinedResponse>(await response.Content.ReadFromJsonAsync<RoomJoinedResponse>());
    }

    private async Task<HttpResponseMessage> PostAuthorizedAsync(
        string path,
        string token,
        object body,
        WebApplicationFactory<Program>? app = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await (app ?? factory).CreateClient().SendAsync(request);
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static T? GetProperty<T>(object instance, string propertyName) =>
        (T?)instance.GetType().GetProperty(propertyName)!.GetValue(instance);

    private static void AssertViewerSafeParticipantStats(GameSnapshot snapshot)
    {
        if (snapshot.Combat is null)
        {
            return;
        }

        Assert.Single(snapshot.Combat.Participants, participant => participant.ViewerOwned && participant.Stats is not null);
        Assert.All(
            snapshot.Combat.Participants.Where(participant => !participant.ViewerOwned),
            participant => Assert.Null(participant.Stats));

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(snapshot));
        var participants = document.RootElement.GetProperty("Combat").GetProperty("Participants");
        foreach (var participant in participants.EnumerateArray())
        {
            var viewerOwned = participant.GetProperty("ViewerOwned").GetBoolean();
            var stats = participant.GetProperty("Stats");
            if (!viewerOwned)
            {
                Assert.Equal(JsonValueKind.Null, stats.ValueKind);
                continue;
            }

            Assert.Equal(JsonValueKind.Object, stats.ValueKind);
            Assert.Equal(
                ["Dex", "Dodge", "Fighting"],
                stats.EnumerateObject().Select(property => property.Name).Order().ToArray());
        }
    }

    private static Array CreateOpponentDefinitions(GameCoordinator coordinator)
    {
        var type = typeof(GameCoordinator).Assembly.GetType("Trpg.Multiplayer.Api.Gameplay.OpponentDefinition")!;
        var definitions = Array.CreateInstance(type, 1);
        definitions.SetValue(
            Activator.CreateInstance(
                type,
                "Cultist",
                70,
                55,
                40,
                new[] { CombatResponse.Dodge, CombatResponse.FightBack },
                1,
                CombatResponse.Dodge,
                90,
                90,
                12,
                12,
                0,
                new CombatWeaponProfile(
                    "trusted-maul",
                    "重击",
                    new DiceExpression("1d12", 1, 12, 0),
                    false,
                    "melee_non_impaling")),
            0);
        return definitions;
    }

    private static (string ExchangeId, MultiplayerGameState State) SeedPendingDamageDisposition(
        IGameStateStore stateStore,
        Guid roomId,
        string ownerParticipantId,
        string targetParticipantId)
    {
        Assert.True(stateStore.TryGet(roomId, out var state));
        var exchangeId = "task9-damage";
        var disposition = new DamageDispositionState(
            new DamageDispositionData(
                exchangeId,
                new CombatParticipantId(ownerParticipantId),
                new CombatParticipantId(targetParticipantId),
                CombatDamageMode.InitiatorExtremeEligible,
                state!.Revision),
            DamageDispositionStatus.Pending,
            null);
        var historicalExchange = new CombatExchange(
            exchangeId,
            state.Combat!.Round,
            state.Combat.TurnIndex,
            new CombatParticipantId(ownerParticipantId),
            new CombatParticipantId(targetParticipantId),
            CombatResponse.Dodge,
            new CheckResolutionResult(1, 99, "regular", 99, "success", true, false, false),
            new CheckResolutionResult(100, 99, "regular", 99, "failure", false, false, false),
            0,
            0,
            1,
            "attacker_hits",
            new CombatParticipantId(ownerParticipantId),
            disposition,
            DateTimeOffset.UtcNow);
        var replacement = new MultiplayerGameState(
            state.RoomId,
            state.Revision,
            state.Status,
            state.CreatedAt,
            state.Characters,
            state.LastCheck,
            state.Combat! with
            {
                LastExchange = historicalExchange,
                History = state.Combat.History.Append(historicalExchange).ToArray(),
                DamageDispositions = new Dictionary<string, DamageDispositionState>(StringComparer.Ordinal)
                {
                    [exchangeId] = disposition
                }
            });
        Assert.True(stateStore.TryReplace(state, replacement));
        return (exchangeId, replacement);
    }

    private static bool ReadBooleanProperty(object value, string propertyName) =>
        Assert.IsType<bool>(value.GetType().GetProperty(propertyName)!.GetValue(value));

    private static async Task<object> InvokeInternalCombatAsync(GameCoordinator coordinator, string methodName, params object?[] arguments)
    {
        var method = typeof(GameCoordinator).GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var command = Activator.CreateInstance(method.GetParameters().Single().ParameterType, arguments)!;
        var task = Assert.IsAssignableFrom<Task>(method.Invoke(coordinator, [command]));
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var isSuccess = Assert.IsType<bool>(result.GetType().GetProperty("IsSuccess")!.GetValue(result));
        var error = result.GetType().GetProperty("Error")!.GetValue(result);
        var errorCode = error?.GetType().GetProperty("Code")?.GetValue(error);
        Assert.True(isSuccess, $"{methodName} failed with {errorCode}.");
        return result;
    }

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

    private sealed record PlayerCombatSetup(
        RoomCreatedResponse Room,
        RoomJoinedResponse Defender,
        RoomJoinedResponse Observer,
        Guid ActorCharacterId);

    private sealed record ObservedSnapshot(GameSnapshot Snapshot, bool Committed);

    private sealed record CombatDelivery(
        long RequestRevision,
        IReadOnlyList<ObservedSnapshot> Actor,
        IReadOnlyList<ObservedSnapshot> Defender,
        IReadOnlyList<ObservedSnapshot> Nonparticipant,
        GameSnapshot HttpSnapshot,
        string? ExchangeId,
        IReadOnlyList<long> PublishedRevisions);

    private sealed class CommitTrackingGameRealtimeNotifier(
        IHubContext<RoomHub, IRoomClient> hubContext,
        IGameStateStore states,
        IPlayerConnectionRegistry connections) : IGameRealtimeNotifier
    {
        private readonly SignalRGameRealtimeNotifier inner = new(hubContext, states, connections);
        private readonly ConcurrentDictionary<long, byte> committedBeforePublish = new();
        private readonly ConcurrentQueue<long> publishedRevisions = new();

        public async Task PublishGameSnapshotAsync(Guid roomId)
        {
            if (states.TryGet(roomId, out var state) && state is not null)
            {
                committedBeforePublish.TryAdd(state.Revision, 0);
                publishedRevisions.Enqueue(state.Revision);
            }

            await inner.PublishGameSnapshotAsync(roomId);
        }

        public Task PublishCheckResolvedAsync(Guid roomId, CheckResolvedEvent message) =>
            inner.PublishCheckResolvedAsync(roomId, message);

        public Task SendGameSnapshotAsync(string connectionId, Guid roomId, Guid playerId) =>
            inner.SendGameSnapshotAsync(connectionId, roomId, playerId);

        public bool WasCommittedBeforePublish(long revision) => committedBeforePublish.ContainsKey(revision);

        public IReadOnlyList<long> GetPublishedRevisions() => publishedRevisions.ToArray();
    }

    private sealed class SequenceDiceRoller(
        IEnumerable<int> percentileRolls,
        IEnumerable<int> genericRolls) : IDiceRoller
    {
        private readonly Queue<int> percentileRolls = new(percentileRolls);
        private readonly Queue<int> genericRolls = new(genericRolls);

        public PercentileDiceRoll RollPercentile(int bonusDice, int penaltyDice)
        {
            Assert.True(percentileRolls.TryDequeue(out var roll), "No deterministic percentile roll remains.");
            return new PercentileDiceRoll(roll, [roll]);
        }

        public GenericDiceRoll RollDice(DiceRollRequest request)
        {
            var rolls = new int[request.Count];
            for (var index = 0; index < rolls.Length; index++)
            {
                Assert.True(genericRolls.TryDequeue(out rolls[index]), "No deterministic generic roll remains.");
            }

            return new GenericDiceRoll(request.Count, request.Faces, rolls, rolls.Sum());
        }
    }
}
