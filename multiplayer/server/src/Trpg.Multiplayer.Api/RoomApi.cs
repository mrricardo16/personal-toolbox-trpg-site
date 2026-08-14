using System.Net;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api;

public static class RoomApi
{
    public static void MapRoomEndpoints(this WebApplication app)
    {
        app.MapPost("/api/rooms", CreateAsync);
        app.MapPost("/api/rooms/join", JoinAsync);
        app.MapPost("/api/rooms/{roomId:guid}/leave", LeaveAsync);
        app.MapPost("/api/rooms/{roomId:guid}/ready", SetReadyAsync);
    }

    private static async Task<IResult> CreateAsync(CreateRoomRequest? request, RoomCoordinator coordinator, IInviteCodeRegistry inviteCodes, IPlayerSessionStore sessions)
    {
        if (request is not { Nickname: { } nickname } || !IsValid(nickname) || request.MaxPlayers <= 0)
        {
            return Results.BadRequest();
        }

        var playerId = Guid.NewGuid();
        var result = await coordinator.CreateAsync(new CreateRoomCommand(playerId, nickname, request.MaxPlayers));
        if (!result.IsSuccess)
        {
            return ToError(result.Error!.Code);
        }

        var room = result.Value!;
        if (!inviteCodes.TryRegister(room.RoomId, out var inviteCode))
        {
            await coordinator.LeaveAsync(new LeaveRoomCommand(room.RoomId, playerId));
            return Results.StatusCode((int)HttpStatusCode.ServiceUnavailable);
        }

        var token = sessions.Create(playerId, room.RoomId, true);
        return Results.Created($"/api/rooms/{room.RoomId}", new RoomCreatedResponse(room.RoomId, inviteCode, playerId, token, ToSnapshot(room, inviteCode)));
    }

    private static async Task<IResult> JoinAsync(JoinRoomRequest? request, RoomCoordinator coordinator, IInviteCodeRegistry inviteCodes, IPlayerSessionStore sessions)
    {
        if (request is not { Nickname: { } nickname, InviteCode: { } suppliedInviteCode } || !IsValid(nickname) || string.IsNullOrWhiteSpace(suppliedInviteCode))
        {
            return Results.BadRequest();
        }

        var normalizedCode = suppliedInviteCode.Trim().ToUpperInvariant();
        if (!inviteCodes.TryGetRoomId(normalizedCode, out var roomId))
        {
            return Results.NotFound();
        }

        var playerId = Guid.NewGuid();
        var result = await coordinator.JoinAsync(new JoinRoomCommand(roomId, playerId, nickname));
        if (!result.IsSuccess)
        {
            if (result.Error!.Code == RoomErrorCode.RoomNotFound)
            {
                inviteCodes.Remove(roomId);
            }

            return ToError(result.Error.Code);
        }

        var token = sessions.Create(playerId, roomId, false);
        return Results.Ok(new RoomJoinedResponse(playerId, token, ToSnapshot(result.Value!, normalizedCode)));
    }

    private static async Task<IResult> LeaveAsync(Guid roomId, HttpRequest request, RoomCoordinator coordinator, IInviteCodeRegistry inviteCodes, IPlayerSessionStore sessions)
    {
        if (!TryGetSession(request, sessions, out var token, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        var result = await coordinator.LeaveAsync(new LeaveRoomCommand(roomId, session.PlayerId));
        if (!result.IsSuccess)
        {
            return ToError(result.Error!.Code);
        }

        if (result.Value!.RoomWasClosed)
        {
            sessions.RemoveByRoom(roomId);
            inviteCodes.Remove(roomId);
            return Results.Ok(new { roomWasClosed = true });
        }

        sessions.Remove(token!);
        return Results.Ok(ToSnapshot(result.Value.Room!, GetInviteCode(inviteCodes, roomId)));
    }

    private static async Task<IResult> SetReadyAsync(Guid roomId, SetReadyRequest? request, HttpRequest httpRequest, RoomCoordinator coordinator, IInviteCodeRegistry inviteCodes, IPlayerSessionStore sessions)
    {
        if (request is null)
        {
            return Results.BadRequest();
        }

        if (!TryGetSession(httpRequest, sessions, out _, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        var result = await coordinator.SetReadyAsync(new SetRoomReadyCommand(roomId, session.PlayerId, request.IsReady));
        return result.IsSuccess
            ? Results.Ok(ToSnapshot(result.Value!, GetInviteCode(inviteCodes, roomId)))
            : ToError(result.Error!.Code);
    }

    private static bool TryGetSession(HttpRequest request, IPlayerSessionStore sessions, out string? token, out PlayerSessionContext? session)
    {
        token = null;
        session = null;
        var authorization = request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        if (!authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorization[bearerPrefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(token) && sessions.TryGet(token, out session);
    }

    private static bool IsValid(string? nickname) => !string.IsNullOrWhiteSpace(nickname);

    private static string GetInviteCode(IInviteCodeRegistry inviteCodes, Guid roomId) =>
        inviteCodes.TryGetInviteCode(roomId, out var inviteCode) ? inviteCode! : string.Empty;

    private static IResult ToError(RoomErrorCode errorCode) => errorCode switch
    {
        RoomErrorCode.InvalidNickname or RoomErrorCode.InvalidMaxPlayers => Results.BadRequest(),
        RoomErrorCode.RoomNotFound => Results.NotFound(),
        RoomErrorCode.RoomFull or RoomErrorCode.PlayerAlreadyExists or RoomErrorCode.RoomClosed => Results.Conflict(),
        RoomErrorCode.PlayerNotFound or RoomErrorCode.NotMember => Results.StatusCode((int)HttpStatusCode.Forbidden),
        _ => Results.StatusCode((int)HttpStatusCode.InternalServerError)
    };

    private static RoomSnapshot ToSnapshot(RoomSession room, string inviteCode) => new(
        room.RoomId,
        inviteCode,
        room.HostPlayerId,
        room.MaxPlayers,
        room.Status.ToString(),
        room.Revision,
        room.Players.Select(player => new PlayerSnapshot(player.PlayerId, player.Nickname, player.IsHost, player.IsReady, player.IsConnected)).ToArray());
}

public sealed record CreateRoomRequest(string? Nickname, int MaxPlayers);
public sealed record JoinRoomRequest(string? InviteCode, string? Nickname);
public sealed record SetReadyRequest(bool IsReady);
public sealed record RoomCreatedResponse(Guid RoomId, string InviteCode, Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
public sealed record RoomJoinedResponse(Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
public sealed record RoomSnapshot(Guid RoomId, string InviteCode, Guid HostPlayerId, int MaxPlayers, string Status, long Revision, IReadOnlyList<PlayerSnapshot> Players);
public sealed record PlayerSnapshot(Guid PlayerId, string Nickname, bool IsHost, bool IsReady, bool IsConnected);
