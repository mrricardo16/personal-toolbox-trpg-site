using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api;

public static class GameApi
{
    private static readonly JsonSerializerOptions CombatRequestJsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapGameEndpoints(this WebApplication app)
    {
        app.MapPost("/api/rooms/{roomId:guid}/game/initialize", InitializeAsync);
        app.MapGet("/api/rooms/{roomId:guid}/game", GetProjectionAsync);
        app.MapPost("/api/rooms/{roomId:guid}/game/check", ResolveCheckAsync);
        app.MapPost("/api/rooms/{roomId:guid}/game/combat/melee-attack", MeleeAttackAsync);
        app.MapPost("/api/rooms/{roomId:guid}/game/combat/respond", RespondAsync);
        app.MapPost("/api/rooms/{roomId:guid}/game/combat/pass", PassCombatAsync);
    }

    private static async Task<IResult> MeleeAttackAsync(
        Guid roomId,
        HttpRequest httpRequest,
        IPlayerSessionStore sessions,
        RoomMutationDeliveryGate mutationGate,
        IPlayerCombatIntentCoordinator intents,
        ILoggerFactory loggerFactory)
    {
        if (!TryGetSession(httpRequest, sessions, out var session))
        {
            return CombatIntentError(StatusCodes.Status401Unauthorized, "invalid_session");
        }

        if (session!.RoomId != roomId)
        {
            return CombatIntentError(StatusCodes.Status403Forbidden, "not_member");
        }

        if (!httpRequest.HasJsonContentType())
        {
            return CombatIntentError(StatusCodes.Status400BadRequest, "invalid_intent");
        }

        var request = await ReadCombatRequestAsync<MeleeAttackRequest>(httpRequest);
        if (request is null
            || request.ExpectedGameRevision <= 0
            || request.ActorCharacterId == Guid.Empty
            || string.IsNullOrWhiteSpace(request.TargetParticipantId))
        {
            return CombatIntentError(StatusCodes.Status400BadRequest, "invalid_intent");
        }

        return await RunCombatIntentAsync(
            roomId,
            mutationGate,
            loggerFactory,
            () => intents.MeleeAttackAsync(new PlayerMeleeAttackIntent(
                roomId,
                session.PlayerId,
                request.ExpectedGameRevision,
                request.ActorCharacterId,
                request.TargetParticipantId.Trim())));
    }

    private static async Task<IResult> RespondAsync(
        Guid roomId,
        HttpRequest httpRequest,
        IPlayerSessionStore sessions,
        RoomMutationDeliveryGate mutationGate,
        IPlayerCombatIntentCoordinator intents,
        ILoggerFactory loggerFactory)
    {
        if (!TryGetSession(httpRequest, sessions, out var session))
        {
            return CombatIntentError(StatusCodes.Status401Unauthorized, "invalid_session");
        }

        if (session!.RoomId != roomId)
        {
            return CombatIntentError(StatusCodes.Status403Forbidden, "not_member");
        }

        if (!httpRequest.HasJsonContentType())
        {
            return CombatIntentError(StatusCodes.Status400BadRequest, "invalid_intent");
        }

        var request = await ReadCombatRequestAsync<CombatRespondRequest>(httpRequest);
        if (request is null
            || request.ExpectedGameRevision <= 0
            || string.IsNullOrWhiteSpace(request.ExchangeId))
        {
            return CombatIntentError(StatusCodes.Status400BadRequest, "invalid_intent");
        }

        if (!TryParseCombatResponse(request.Response, out var response))
        {
            return CombatIntentError(StatusCodes.Status400BadRequest, "invalid_response");
        }

        return await RunCombatIntentAsync(
            roomId,
            mutationGate,
            loggerFactory,
            () => intents.RespondAsync(new PlayerRespondIntent(
                roomId,
                session.PlayerId,
                request.ExpectedGameRevision,
                request.ExchangeId.Trim(),
                response)));
    }

    private static async Task<IResult> PassCombatAsync(
        Guid roomId,
        HttpRequest httpRequest,
        IPlayerSessionStore sessions,
        RoomMutationDeliveryGate mutationGate,
        IPlayerCombatIntentCoordinator intents,
        ILoggerFactory loggerFactory)
    {
        if (!TryGetSession(httpRequest, sessions, out var session))
        {
            return CombatIntentError(StatusCodes.Status401Unauthorized, "invalid_session");
        }

        if (session!.RoomId != roomId)
        {
            return CombatIntentError(StatusCodes.Status403Forbidden, "not_member");
        }

        if (!httpRequest.HasJsonContentType())
        {
            return CombatIntentError(StatusCodes.Status400BadRequest, "invalid_intent");
        }

        var request = await ReadCombatRequestAsync<CombatPassRequest>(httpRequest);
        if (request is null || request.ExpectedGameRevision <= 0 || request.ActorCharacterId == Guid.Empty)
        {
            return CombatIntentError(StatusCodes.Status400BadRequest, "invalid_intent");
        }

        return await RunCombatIntentAsync(
            roomId,
            mutationGate,
            loggerFactory,
            () => intents.PassAsync(new PlayerPassIntent(
                roomId,
                session.PlayerId,
                request.ExpectedGameRevision,
                request.ActorCharacterId)));
    }

    private static async Task<IResult> RunCombatIntentAsync(
        Guid roomId,
        RoomMutationDeliveryGate mutationGate,
        ILoggerFactory loggerFactory,
        Func<Task<PlayerCombatIntentResult>> invokeIntent)
    {
        return await mutationGate.RunAsync(roomId, async () =>
        {
            try
            {
                var result = await invokeIntent();
                return result.IsSuccess
                    ? Results.Ok(result.Snapshot)
                    : ToCombatIntentError(result.Error!, roomId, loggerFactory);
            }
            catch (CombatDamageCommitInvariantException exception)
            {
                return CombatInvariantError(roomId, loggerFactory, exception);
            }
            catch (CombatDamageStateInvariantException exception)
            {
                return CombatInvariantError(roomId, loggerFactory, exception);
            }
            catch (PlayerCombatIntentInvariantException exception)
            {
                return CombatInvariantError(roomId, loggerFactory, exception);
            }
        });
    }

    private static IResult CombatInvariantError(
        Guid roomId,
        ILoggerFactory loggerFactory,
        Exception exception)
    {
        loggerFactory.CreateLogger(typeof(GameApi).FullName ?? nameof(GameApi)).LogError(
            exception,
            "Combat intent invariant failure. RoomId: {RoomId}",
            roomId);
        return CombatIntentError(
            StatusCodes.Status500InternalServerError,
            "combat_consistency_failure");
    }

    private static async Task<T?> ReadCombatRequestAsync<T>(HttpRequest request)
        where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                request.Body,
                CombatRequestJsonOptions,
                request.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IResult CombatIntentError(int statusCode, string code) => Results.Json(
        new CombatIntentErrorResponse(code),
        statusCode: statusCode);

    private static async Task<IResult> InitializeAsync(
        Guid roomId,
        InitializeGameRequest? request,
        HttpRequest httpRequest,
        IPlayerSessionStore sessions,
        IGameCoordinator games,
        RoomMutationDeliveryGate mutationGate,
        IGameRealtimeNotifier notifier)
    {
        if (request is null || request.Characters is null)
        {
            return Results.BadRequest();
        }

        if (!TryGetSession(httpRequest, sessions, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        return await mutationGate.RunAsync(roomId, async () =>
        {
            var result = await games.InitializeAsync(new InitializeGameCommand(
                roomId,
                session.PlayerId,
                request.Characters
                    .Select(character => new InitializeCharacterCommand(
                        character.PlayerId,
                        character.Name ?? string.Empty,
                        character.CheckValues ?? new Dictionary<string, int>(),
                        character.Health is null
                            ? null
                            : new CharacterHealthSetup(character.Health.CurrentHp, character.Health.MaxHp, character.Health.Con)))
                    .ToArray()));
            if (!result.IsSuccess)
            {
                return ToError(result.Error!.Code);
            }

            var projection = await games.GetProjectionAsync(roomId, session.PlayerId);
            if (!projection.IsSuccess)
            {
                return ToError(projection.Error!.Code);
            }

            await notifier.PublishGameSnapshotAsync(roomId);
            return Results.Created($"/api/rooms/{roomId}/game", projection.Value);
        });
    }

    private static async Task<IResult> GetProjectionAsync(
        Guid roomId,
        HttpRequest httpRequest,
        IPlayerSessionStore sessions,
        IGameCoordinator games,
        RoomMutationDeliveryGate mutationGate)
    {
        if (!TryGetSession(httpRequest, sessions, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        return await mutationGate.RunAsync(roomId, async () =>
        {
            var result = await games.GetProjectionAsync(roomId, session.PlayerId);
            return result.IsSuccess ? Results.Ok(result.Value) : ToError(result.Error!.Code);
        });
    }

    private static async Task<IResult> ResolveCheckAsync(
        Guid roomId,
        ResolveCheckRequest? request,
        HttpRequest httpRequest,
        IPlayerSessionStore sessions,
        IGameCoordinator games,
        RoomMutationDeliveryGate mutationGate,
        IGameRealtimeNotifier notifier)
    {
        if (request is null || request.CharacterId == Guid.Empty || string.IsNullOrWhiteSpace(request.CheckKey))
        {
            return Results.BadRequest();
        }

        if (!TryGetSession(httpRequest, sessions, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        return await mutationGate.RunAsync(roomId, async () =>
        {
            var result = await games.ResolveCheckAsync(new ResolveCheckCommand(
                roomId,
                session.PlayerId,
                request.CharacterId,
                request.CheckKey.Trim(),
                string.IsNullOrWhiteSpace(request.Difficulty) ? "regular" : request.Difficulty.Trim(),
                request.BonusDice,
                request.PenaltyDice));
            if (!result.IsSuccess)
            {
                return ToError(result.Error!.Code);
            }

            var check = result.Value!.Snapshot.LastCheck!;
            await notifier.PublishGameSnapshotAsync(roomId);
            await notifier.PublishCheckResolvedAsync(
                roomId,
                new CheckResolvedEvent(
                    roomId,
                    check.CheckId,
                    check.PlayerId,
                    check.CharacterId,
                    check.CheckKey,
                    check.GameRevision));
            return Results.Ok(result.Value);
        });
    }

    private static bool TryGetSession(HttpRequest request, IPlayerSessionStore sessions, out PlayerSessionContext? session)
    {
        session = null;
        const string bearerPrefix = "Bearer ";
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = authorization[bearerPrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(token) && sessions.TryGet(token, out session);
    }

    private static bool TryParseCombatResponse(string? value, out CombatResponse response)
    {
        response = value switch
        {
            "dodge" => CombatResponse.Dodge,
            "fight_back" => CombatResponse.FightBack,
            _ => default
        };
        return value is "dodge" or "fight_back";
    }

    private static IResult ToCombatIntentError(
        PlayerCombatIntentError error,
        Guid roomId,
        ILoggerFactory loggerFactory)
    {
        var (statusCode, wireCode) = error.Code switch
        {
            PlayerCombatIntentErrorCode.InvalidIntent => (StatusCodes.Status400BadRequest, "invalid_intent"),
            PlayerCombatIntentErrorCode.InvalidResponse => (StatusCodes.Status400BadRequest, "invalid_response"),
            PlayerCombatIntentErrorCode.InvalidSession => (StatusCodes.Status401Unauthorized, "invalid_session"),
            PlayerCombatIntentErrorCode.NotMember => (StatusCodes.Status403Forbidden, "not_member"),
            PlayerCombatIntentErrorCode.ActorNotOwned => (StatusCodes.Status403Forbidden, "actor_not_owned"),
            PlayerCombatIntentErrorCode.DefenderNotOwned => (StatusCodes.Status403Forbidden, "defender_not_owned"),
            PlayerCombatIntentErrorCode.RoomNotFound => (StatusCodes.Status404NotFound, "room_not_found"),
            PlayerCombatIntentErrorCode.GameNotFound => (StatusCodes.Status404NotFound, "game_not_found"),
            PlayerCombatIntentErrorCode.StaleGameRevision => (StatusCodes.Status409Conflict, "stale_game_revision"),
            PlayerCombatIntentErrorCode.CombatInactive => (StatusCodes.Status409Conflict, "combat_inactive"),
            PlayerCombatIntentErrorCode.NotCurrentActor => (StatusCodes.Status409Conflict, "not_current_actor"),
            PlayerCombatIntentErrorCode.TargetNotEligible => (StatusCodes.Status409Conflict, "target_not_eligible"),
            PlayerCombatIntentErrorCode.ExchangeNotPending => (StatusCodes.Status409Conflict, "exchange_not_pending"),
            PlayerCombatIntentErrorCode.ProgressionBlocked => (StatusCodes.Status409Conflict, "progression_blocked"),
            PlayerCombatIntentErrorCode.CombatConsistencyFailure => (StatusCodes.Status500InternalServerError, "combat_consistency_failure"),
            _ => (StatusCodes.Status500InternalServerError, "combat_consistency_failure")
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            loggerFactory.CreateLogger(typeof(GameApi).FullName ?? nameof(GameApi)).LogError(
                "Combat intent coordinator reported a consistency failure. RoomId: {RoomId}",
                roomId);
            return CombatIntentError(statusCode, wireCode);
        }

        return Results.Json(
            new CombatIntentErrorResponse(wireCode, error.CurrentGameRevision),
            statusCode: statusCode);
    }

    private static IResult ToError(GameErrorCode code) => code switch
    {
        GameErrorCode.RoomNotFound or GameErrorCode.GameNotFound or GameErrorCode.CharacterNotFound => Results.NotFound(),
        GameErrorCode.RoomClosed or GameErrorCode.AlreadyInitialized => Results.Conflict(),
        GameErrorCode.NotMember or GameErrorCode.NotHost or GameErrorCode.CharacterNotOwned => Results.StatusCode((int)HttpStatusCode.Forbidden),
        GameErrorCode.InvalidRoster or GameErrorCode.UnknownPlayer or GameErrorCode.DuplicateCharacterOwnership or GameErrorCode.InvalidCheckKey or GameErrorCode.InvalidCheckRequest or GameErrorCode.InvalidHealthSetup => Results.BadRequest(),
        GameErrorCode.StateConflict => Results.Conflict(),
        _ => Results.StatusCode((int)HttpStatusCode.InternalServerError)
    };
}

public sealed class InitializeGameRequest
{
    public IReadOnlyList<InitializeCharacterRequest>? Characters { get; init; }
}

public sealed class InitializeCharacterRequest
{
    public Guid PlayerId { get; init; }

    public string? Name { get; init; }

    public Dictionary<string, int>? CheckValues { get; init; }

    public InitializeCharacterHealthRequest? Health { get; init; }
}

public sealed class InitializeCharacterHealthRequest
{
    public int CurrentHp { get; init; }

    public int MaxHp { get; init; }

    public int Con { get; init; }
}

public sealed class ResolveCheckRequest
{
    public Guid CharacterId { get; init; }

    public string? CheckKey { get; init; }

    public string? Difficulty { get; init; }

    public int BonusDice { get; init; }

    public int PenaltyDice { get; init; }
}

public sealed record MeleeAttackRequest(
    long ExpectedGameRevision,
    Guid ActorCharacterId,
    string? TargetParticipantId);

public sealed record CombatRespondRequest(
    long ExpectedGameRevision,
    string? ExchangeId,
    string? Response);

public sealed record CombatPassRequest(
    long ExpectedGameRevision,
    Guid ActorCharacterId);

public sealed record CombatIntentErrorResponse(
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CurrentGameRevision = null);
