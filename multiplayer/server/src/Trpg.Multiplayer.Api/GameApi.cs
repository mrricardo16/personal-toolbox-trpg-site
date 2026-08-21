using System.Net;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api;

public static class GameApi
{
    public static void MapGameEndpoints(this WebApplication app)
    {
        app.MapPost("/api/rooms/{roomId:guid}/game/initialize", InitializeAsync);
        app.MapGet("/api/rooms/{roomId:guid}/game", GetProjectionAsync);
        app.MapPost("/api/rooms/{roomId:guid}/game/check", ResolveCheckAsync);
    }

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
