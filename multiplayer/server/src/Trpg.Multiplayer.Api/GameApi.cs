using System.Net;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api;

public static class GameApi
{
    public static void MapGameEndpoints(this WebApplication app)
    {
        app.MapPost("/api/rooms/{roomId:guid}/game/initialize", InitializeAsync);
        app.MapGet("/api/rooms/{roomId:guid}/game", GetProjectionAsync);
    }

    private static async Task<IResult> InitializeAsync(
        Guid roomId,
        InitializeGameRequest? request,
        HttpRequest httpRequest,
        IPlayerSessionStore sessions,
        IGameCoordinator games)
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

        var result = await games.InitializeAsync(new InitializeGameCommand(
            roomId,
            session.PlayerId,
            request.Characters
                .Select(character => new InitializeCharacterCommand(
                    character.PlayerId,
                    character.Name ?? string.Empty,
                    character.CheckValues ?? new Dictionary<string, int>()))
                .ToArray()));
        if (!result.IsSuccess)
        {
            return ToError(result.Error!.Code);
        }

        var projection = await games.GetProjectionAsync(roomId, session.PlayerId);
        return projection.IsSuccess
            ? Results.Created($"/api/rooms/{roomId}/game", projection.Value)
            : ToError(projection.Error!.Code);
    }

    private static async Task<IResult> GetProjectionAsync(
        Guid roomId,
        HttpRequest httpRequest,
        IPlayerSessionStore sessions,
        IGameCoordinator games)
    {
        if (!TryGetSession(httpRequest, sessions, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        var result = await games.GetProjectionAsync(roomId, session.PlayerId);
        return result.IsSuccess ? Results.Ok(result.Value) : ToError(result.Error!.Code);
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
        GameErrorCode.InvalidRoster or GameErrorCode.UnknownPlayer or GameErrorCode.DuplicateCharacterOwnership => Results.BadRequest(),
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
}
