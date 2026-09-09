using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    [Fact]
    public async Task StabilizationActions_AreNotPublicGameRoutes()
    {
        var client = factory.CreateClient();

        var dyingRound = await client.PostAsJsonAsync(
            "/api/rooms/00000000-0000-0000-0000-000000000001/game/dying-round",
            new { characterId = Guid.NewGuid(), sourceId = "route-probe" });
        var firstAid = await client.PostAsJsonAsync(
            "/api/rooms/00000000-0000-0000-0000-000000000001/game/first-aid",
            new { characterId = Guid.NewGuid(), target = 60, withinHour = true, sourceId = "route-probe" });
        var stabilize = await client.PostAsJsonAsync(
            "/api/rooms/00000000-0000-0000-0000-000000000001/game/stabilize",
            new { characterId = Guid.NewGuid(), sourceId = "route-probe" });

        Assert.Equal(HttpStatusCode.NotFound, dyingRound.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, firstAid.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stabilize.StatusCode);
    }

    [Fact]
    public void CombatIntentRoutes_MapExactlyMeleeAttackRespondAndPass()
    {
        var endpointDataSource = factory.Services.GetRequiredService<EndpointDataSource>();
        var combatEndpoints = endpointDataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/rooms/{roomId:guid}/game/combat/",
                StringComparison.Ordinal) == true)
            .OrderBy(endpoint => endpoint.RoutePattern.RawText, StringComparer.Ordinal)
            .ToArray();
        var combatRoutes = combatEndpoints
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .ToArray();

        Assert.Equal(
            [
                "/api/rooms/{roomId:guid}/game/combat/melee-attack",
                "/api/rooms/{roomId:guid}/game/combat/pass",
                "/api/rooms/{roomId:guid}/game/combat/respond"
            ],
            combatRoutes);
        Assert.All(combatEndpoints, endpoint => Assert.Equal(
            [HttpMethods.Post],
            endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods));
    }

    [Fact]
    public async Task CombatIntentRoutes_StartEndDamageAndResolveDamageRemainNotFound()
    {
        var client = factory.CreateClient();
        var roomId = Guid.NewGuid();
        var forbiddenPaths = new[]
        {
            "combat/start",
            "combat/end",
            "combat/damage",
            "combat/resolve-damage",
            "combat/npc",
            "combat/fire",
            "combat/weapon",
            "damage",
            "resolve-damage",
            "npc",
            "fire",
            "weapon"
        };

        foreach (var path in forbiddenPaths)
        {
            var response = await client.PostAsJsonAsync($"/api/rooms/{roomId}/game/{path}", new { });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Fact]
    public async Task CombatIntentRoutes_RequireBearerSessionAndMatchingRoom()
    {
        var (isolatedFactory, proxy) = CreateCombatIntentFactory();
        await using var factoryLease = isolatedFactory;
        var client = isolatedFactory.CreateClient();
        var first = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "First", maxPlayers = 2 }));
        var second = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "Second", maxPlayers = 2 }));
        proxy.ResultFactory = _ => CreateCombatIntentSuccess(CreateSnapshot(first.RoomId, first.PlayerId, 8));

        var missing = await client.PostAsJsonAsync(
            $"/api/rooms/{first.RoomId}/game/combat/pass",
            new { expectedGameRevision = 7, actorCharacterId = Guid.NewGuid() });
        var crossRoom = await SendAuthorizedAsync(
            client,
            HttpMethod.Post,
            $"/api/rooms/{second.RoomId}/game/combat/pass",
            first.PlayerSessionToken,
            new { expectedGameRevision = 7, actorCharacterId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossRoom.StatusCode);
        Assert.Empty(proxy.Invocations);
    }

    [Fact]
    public async Task CombatIntentRoutes_RequestBodiesCannotSpoofPlayerOrCanonicalFacts()
    {
        var (isolatedFactory, proxy) = CreateCombatIntentFactory();
        await using var factoryLease = isolatedFactory;
        var client = isolatedFactory.CreateClient();
        var created = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        var snapshot = CreateSnapshot(created.RoomId, created.PlayerId, 10);
        proxy.ResultFactory = _ => CreateCombatIntentSuccess(snapshot);
        var actorCharacterId = snapshot.Characters.Single().CharacterId;

        var melee = await SendAuthorizedAsync(client, HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/combat/melee-attack", created.PlayerSessionToken,
            new
            {
                expectedGameRevision = 7,
                actorCharacterId,
                targetParticipantId = "opponent:0",
                playerId = Guid.NewGuid(),
                ownerPlayerId = Guid.NewGuid(),
                requestingPlayerId = Guid.NewGuid(),
                roll = 1,
                target = 99,
                responsePolicy = "fight_back",
                damage = 99,
                nextActor = "spoof",
                round = 99
            });
        var respond = await SendAuthorizedAsync(client, HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/combat/respond", created.PlayerSessionToken,
            new
            {
                expectedGameRevision = 8,
                exchangeId = "exchange-1",
                response = "dodge",
                playerId = Guid.NewGuid(),
                ownerPlayerId = Guid.NewGuid(),
                requestingPlayerId = Guid.NewGuid(),
                roll = 1,
                target = 99,
                responsePolicy = "fight_back",
                damage = 99,
                nextActor = "spoof",
                round = 99
            });
        var pass = await SendAuthorizedAsync(client, HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/combat/pass", created.PlayerSessionToken,
            new
            {
                expectedGameRevision = 9,
                actorCharacterId,
                playerId = Guid.NewGuid(),
                ownerPlayerId = Guid.NewGuid(),
                requestingPlayerId = Guid.NewGuid(),
                roll = 1,
                target = 99,
                responsePolicy = "fight_back",
                damage = 99,
                nextActor = "spoof",
                round = 99
            });

        Assert.All([melee, respond, pass], response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(["MeleeAttackAsync", "RespondAsync", "PassAsync"], proxy.Invocations.Select(call => call.MethodName).ToArray());
        Assert.All(proxy.Invocations, call =>
        {
            Assert.Equal(created.RoomId, GetProperty<Guid>(call.Intent, "RoomId"));
            Assert.Equal(created.PlayerId, GetProperty<Guid>(call.Intent, "PlayerId"));
        });
        Assert.Equal(7, GetProperty<long>(proxy.Invocations[0].Intent, "ExpectedGameRevision"));
        Assert.Equal(actorCharacterId, GetProperty<Guid>(proxy.Invocations[0].Intent, "ActorCharacterId"));
        Assert.Equal("opponent:0", GetProperty<string>(proxy.Invocations[0].Intent, "TargetParticipantId"));
        Assert.Equal(8, GetProperty<long>(proxy.Invocations[1].Intent, "ExpectedGameRevision"));
        Assert.Equal("exchange-1", GetProperty<string>(proxy.Invocations[1].Intent, "ExchangeId"));
        Assert.Equal("Dodge", GetProperty<object>(proxy.Invocations[1].Intent, "Response").ToString());
        Assert.Equal(9, GetProperty<long>(proxy.Invocations[2].Intent, "ExpectedGameRevision"));
        Assert.Equal(actorCharacterId, GetProperty<Guid>(proxy.Invocations[2].Intent, "ActorCharacterId"));

        AssertRequestContract("MeleeAttackRequest", ["ActorCharacterId", "ExpectedGameRevision", "TargetParticipantId"]);
        AssertRequestContract("CombatRespondRequest", ["ExchangeId", "ExpectedGameRevision", "Response"]);
        AssertRequestContract("CombatPassRequest", ["ActorCharacterId", "ExpectedGameRevision"]);
    }

    [Fact]
    public async Task CombatIntentRoutes_RejectMalformedIdsResponseAndRevision()
    {
        var (isolatedFactory, proxy) = CreateCombatIntentFactory();
        await using var factoryLease = isolatedFactory;
        var client = isolatedFactory.CreateClient();
        var created = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        proxy.ResultFactory = _ => CreateCombatIntentSuccess(CreateSnapshot(created.RoomId, created.PlayerId, 2));
        var cases = new (string Path, object Body)[]
        {
            ("melee-attack", new { expectedGameRevision = 0, actorCharacterId = Guid.NewGuid(), targetParticipantId = "opponent:0" }),
            ("melee-attack", new { expectedGameRevision = 1, actorCharacterId = Guid.Empty, targetParticipantId = "opponent:0" }),
            ("melee-attack", new { expectedGameRevision = 1, actorCharacterId = Guid.NewGuid(), targetParticipantId = "  " }),
            ("respond", new { expectedGameRevision = -1, exchangeId = "exchange-1", response = "dodge" }),
            ("respond", new { expectedGameRevision = 1, exchangeId = " ", response = "dodge" }),
            ("respond", new { expectedGameRevision = 1, exchangeId = "exchange-1", response = "Dodge" }),
            ("respond", new { expectedGameRevision = 1, exchangeId = "exchange-1", response = "block" }),
            ("pass", new { expectedGameRevision = 1, actorCharacterId = Guid.Empty })
        };

        foreach (var (path, body) in cases)
        {
            var response = await SendAuthorizedAsync(client, HttpMethod.Post,
                $"/api/rooms/{created.RoomId}/game/combat/{path}", created.PlayerSessionToken, body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var malformedRoomId = await SendAuthorizedAsync(client, HttpMethod.Post,
            "/api/rooms/not-a-guid/game/combat/pass", created.PlayerSessionToken,
            new { expectedGameRevision = 1, actorCharacterId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, malformedRoomId.StatusCode);
        Assert.Empty(proxy.Invocations);
    }

    [Fact]
    public async Task CombatIntentRoutes_MapAuthorityAndStateFailuresWithoutInternalLeakage()
    {
        var mappings = new (string ApplicationCode, HttpStatusCode Status, string WireCode, long? Revision)[]
        {
            ("InvalidIntent", HttpStatusCode.BadRequest, "invalid_intent", null),
            ("InvalidResponse", HttpStatusCode.BadRequest, "invalid_response", 19),
            ("InvalidSession", HttpStatusCode.Unauthorized, "invalid_session", null),
            ("NotMember", HttpStatusCode.Forbidden, "not_member", null),
            ("ActorNotOwned", HttpStatusCode.Forbidden, "actor_not_owned", 19),
            ("DefenderNotOwned", HttpStatusCode.Forbidden, "defender_not_owned", 19),
            ("RoomNotFound", HttpStatusCode.NotFound, "room_not_found", null),
            ("GameNotFound", HttpStatusCode.NotFound, "game_not_found", null),
            ("CombatInactive", HttpStatusCode.Conflict, "combat_inactive", 19),
            ("NotCurrentActor", HttpStatusCode.Conflict, "not_current_actor", 19),
            ("TargetNotEligible", HttpStatusCode.Conflict, "target_not_eligible", 19),
            ("ExchangeNotPending", HttpStatusCode.Conflict, "exchange_not_pending", 19),
            ("ProgressionBlocked", HttpStatusCode.Conflict, "progression_blocked", 19),
            ("CombatConsistencyFailure", HttpStatusCode.InternalServerError, "combat_consistency_failure", 19)
        };

        var (isolatedFactory, proxy) = CreateCombatIntentFactory();
        await using var factoryLease = isolatedFactory;
        var client = isolatedFactory.CreateClient();
        var created = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));

        foreach (var mapping in mappings)
        {
            proxy.ResultFactory = _ => CreateCombatIntentFailure(mapping.ApplicationCode, mapping.Revision);
            var response = await SendAuthorizedAsync(client, HttpMethod.Post,
                $"/api/rooms/{created.RoomId}/game/combat/pass", created.PlayerSessionToken,
                new { expectedGameRevision = 18, actorCharacterId = Guid.NewGuid() });

            Assert.Equal(mapping.Status, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(mapping.WireCode, json.RootElement.GetProperty("code").GetString());
            Assert.Equal(mapping.Revision.HasValue, json.RootElement.TryGetProperty("currentGameRevision", out var revision));
            if (mapping.Revision.HasValue)
            {
                Assert.Equal(mapping.Revision.Value, revision.GetInt64());
            }
            Assert.DoesNotContain(mapping.ApplicationCode, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.All(json.RootElement.EnumerateObject(), property =>
                Assert.Contains(property.Name, new[] { "code", "currentGameRevision" }));
        }
    }

    [Theory]
    [InlineData("melee-attack", "MeleeAttackAsync")]
    [InlineData("respond", "RespondAsync")]
    [InlineData("pass", "PassAsync")]
    public async Task CombatIntentRoutes_InvariantFailureReturnsSafeStructuredError(
        string route,
        string coordinatorMethod)
    {
        const string sensitiveExceptionText = "secret canonical damage commit detail";
        var (isolatedFactory, proxy) = CreateCombatIntentFactory();
        await using var factoryLease = isolatedFactory;
        var client = isolatedFactory.CreateClient();
        var created = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        proxy.ExceptionFactory = _ => CreateCombatDamageCommitInvariantException(sensitiveExceptionText);
        var actorCharacterId = Guid.NewGuid();
        object body = route switch
        {
            "melee-attack" => new
            {
                expectedGameRevision = 7,
                actorCharacterId,
                targetParticipantId = "opponent:0"
            },
            "respond" => new
            {
                expectedGameRevision = 7,
                exchangeId = "exchange-1",
                response = "dodge"
            },
            _ => new
            {
                expectedGameRevision = 7,
                actorCharacterId
            }
        };

        var response = await SendAuthorizedAsync(client, HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/combat/{route}", created.PlayerSessionToken, body);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(responseBody);
        var property = Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("code", property.Name);
        Assert.Equal("combat_consistency_failure", property.Value.GetString());
        Assert.DoesNotContain(sensitiveExceptionText, responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("CombatDamageCommitInvariantException", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerCombatIntentErrorCode", responseBody, StringComparison.Ordinal);
        Assert.False(json.RootElement.TryGetProperty("currentGameRevision", out _));
        Assert.Equal(coordinatorMethod, Assert.Single(proxy.Invocations).MethodName);
    }

    [Fact]
    public async Task CombatIntentRoutes_StaleRevisionReturnsConflictAndCurrentSafeRevision()
    {
        var (isolatedFactory, proxy) = CreateCombatIntentFactory();
        await using var factoryLease = isolatedFactory;
        var client = isolatedFactory.CreateClient();
        var created = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        proxy.ResultFactory = _ => CreateCombatIntentFailure("StaleGameRevision", 23);

        var response = await SendAuthorizedAsync(client, HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/combat/pass", created.PlayerSessionToken,
            new { expectedGameRevision = 22, actorCharacterId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("stale_game_revision", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(23, json.RootElement.GetProperty("currentGameRevision").GetInt64());
    }

    [Fact]
    public async Task CombatIntentRoutes_SuccessReturnsLatestViewerSpecificGameSnapshot()
    {
        var (isolatedFactory, proxy) = CreateCombatIntentFactory();
        await using var factoryLease = isolatedFactory;
        var client = isolatedFactory.CreateClient();
        var created = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        var latest = CreateSnapshot(created.RoomId, created.PlayerId, 31);
        proxy.ResultFactory = _ => CreateCombatIntentSuccess(latest);

        var response = await SendAuthorizedAsync(client, HttpMethod.Post,
            $"/api/rooms/{created.RoomId}/game/combat/pass", created.PlayerSessionToken,
            new { expectedGameRevision = 30, actorCharacterId = latest.Characters.Single().CharacterId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var returned = Assert.IsType<GameSnapshot>(await response.Content.ReadFromJsonAsync<GameSnapshot>());
        Assert.Equal(latest.RoomId, returned.RoomId);
        Assert.Equal(latest.Revision, returned.Revision);
        Assert.Equal(latest.Status, returned.Status);
        var latestCharacter = latest.Characters.Single();
        var returnedCharacter = returned.Characters.Single();
        Assert.Equal(latestCharacter.CharacterId, returnedCharacter.CharacterId);
        Assert.Equal(latestCharacter.Name, returnedCharacter.Name);
        Assert.Equal(latestCharacter.CheckValues, returnedCharacter.CheckValues);
        Assert.Equal(latestCharacter.Health, returnedCharacter.Health);
        Assert.Equal(created.PlayerId, returnedCharacter.OwnerPlayerId);
        Assert.Single(proxy.Invocations);
    }

    [Fact]
    public async Task CombatIntentRoutes_HostHasNoPublicNpcSuperuserAuthority()
    {
        var (isolatedFactory, proxy) = CreateCombatIntentFactory();
        await using var factoryLease = isolatedFactory;
        var client = isolatedFactory.CreateClient();
        var host = await ReadCreatedAsync(await client.PostAsJsonAsync("/api/rooms", new { nickname = "Host", maxPlayers = 2 }));
        proxy.ResultFactory = _ => CreateCombatIntentFailure("ActorNotOwned", 4);

        var response = await SendAuthorizedAsync(client, HttpMethod.Post,
            $"/api/rooms/{host.RoomId}/game/combat/melee-attack", host.PlayerSessionToken,
            new
            {
                expectedGameRevision = 4,
                actorCharacterId = Guid.NewGuid(),
                targetParticipantId = "npc:0",
                ownerPlayerId = host.PlayerId,
                responsePolicy = "fight_back"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("actor_not_owned", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(host.PlayerId, GetProperty<Guid>(Assert.Single(proxy.Invocations).Intent, "PlayerId"));
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
        return await SendAuthorizedAsync(factory.CreateClient(), method, path, token, body);
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string token,
        object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(request);
    }

    private (WebApplicationFactory<Program> Factory, RecordingCombatIntentProxy Proxy) CreateCombatIntentFactory()
    {
        var contractType = GetRequiredGameplayType("IPlayerCombatIntentCoordinator");
        var proxyObject = DispatchProxy.Create(contractType, typeof(RecordingCombatIntentProxy));
        var proxy = (RecordingCombatIntentProxy)proxyObject;
        var isolatedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll(contractType);
                services.AddSingleton(contractType, proxyObject);
            });
        });
        return (isolatedFactory, proxy);
    }

    private static GameSnapshot CreateSnapshot(Guid roomId, Guid playerId, long revision) => new(
        roomId,
        revision,
        "Active",
        DateTimeOffset.UnixEpoch,
        [
            new CharacterSnapshot(
                Guid.NewGuid(),
                playerId,
                "Viewer",
                new Dictionary<string, int> { ["fighting"] = 65 },
                new CharacterHealthSnapshot(10, 10, false, false, false, false, false))
        ]);

    private static object CreateCombatIntentSuccess(GameSnapshot snapshot)
    {
        var resultType = GetRequiredGameplayType("PlayerCombatIntentResult");
        return resultType.GetMethod("Success", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [snapshot])!;
    }

    private static object CreateCombatIntentFailure(string code, long? currentGameRevision)
    {
        var resultType = GetRequiredGameplayType("PlayerCombatIntentResult");
        var errorCodeType = GetRequiredGameplayType("PlayerCombatIntentErrorCode");
        var parsedCode = Enum.Parse(errorCodeType, code);
        return resultType.GetMethod("Failure", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [parsedCode, currentGameRevision])!;
    }

    private static Exception CreateCombatDamageCommitInvariantException(string message) =>
        (Exception)Activator.CreateInstance(
            GetRequiredGameplayType("CombatDamageCommitInvariantException"),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            [message],
            null)!;

    private static Type GetRequiredGameplayType(string typeName) =>
        typeof(GameCoordinator).Assembly.GetType($"Trpg.Multiplayer.Api.Gameplay.{typeName}")
        ?? throw new InvalidOperationException($"Gameplay type '{typeName}' was not found.");

    private static T GetProperty<T>(object instance, string propertyName) =>
        (T)instance.GetType().GetProperty(propertyName)!.GetValue(instance)!;

    private static void AssertRequestContract(string typeName, string[] expectedProperties)
    {
        var requestType = typeof(GameApi).Assembly.GetType($"Trpg.Multiplayer.Api.{typeName}");
        Assert.NotNull(requestType);
        Assert.Equal(expectedProperties, requestType.GetProperties().Select(property => property.Name).Order().ToArray());
    }

    private static async Task<CreatedResponse> ReadCreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Assert.IsType<CreatedResponse>(await response.Content.ReadFromJsonAsync<CreatedResponse>());
    }

    private sealed record CreatedResponse(Guid RoomId, string InviteCode, Guid PlayerId, string PlayerSessionToken);

    private sealed record JoinedResponse(Guid PlayerId, string PlayerSessionToken);

    public sealed record CombatIntentInvocation(string MethodName, object Intent);

    public class RecordingCombatIntentProxy : DispatchProxy
    {
        public Func<object, object>? ResultFactory { get; set; }

        public Func<object, Exception?>? ExceptionFactory { get; set; }

        public List<CombatIntentInvocation> Invocations { get; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var intent = args![0]!;
            Invocations.Add(new CombatIntentInvocation(targetMethod!.Name, intent));
            var exception = ExceptionFactory?.Invoke(intent);
            if (exception is not null)
            {
                throw exception;
            }

            var result = ResultFactory?.Invoke(intent)
                ?? throw new InvalidOperationException("A combat intent test result was not configured.");
            return typeof(Task)
                .GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(result.GetType())
                .Invoke(null, [result]);
        }
    }
}
