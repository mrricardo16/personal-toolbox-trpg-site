using System.Net;
using Trpg.Multiplayer.Api.Ai;
using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api.Rooms;

namespace Trpg.Multiplayer.Api;

public static class RoomApi
{
    public static void MapRoomEndpoints(this WebApplication app)
    {
        app.MapPost("/api/rooms", CreateAsync);
        app.MapPost("/api/rooms/join", JoinAsync);
        app.MapGet("/api/rooms/{roomId:guid}", GetSnapshotAsync);
        app.MapPost("/api/rooms/{roomId:guid}/leave", LeaveAsync);
        app.MapPost("/api/rooms/{roomId:guid}/ready", SetReadyAsync);
        app.MapPut("/api/rooms/{roomId:guid}/ai-config", SetAiConfigurationAsync);
        app.MapDelete("/api/rooms/{roomId:guid}/credential", RemoveAiCredentialAsync);
        app.MapPost("/api/rooms/{roomId:guid}/ai-config/test", TestAiConnectionAsync);
    }

    private static IResult GetSnapshotAsync(
        Guid roomId,
        HttpRequest request,
        IRoomStore rooms,
        IPlayerSessionStore sessions,
        IInviteCodeRegistry inviteCodes)
    {
        if (!TryGetSession(request, sessions, out _, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        if (!rooms.TryGet(roomId, out var room) || room is null)
        {
            return Results.NotFound();
        }

        if (!room.Players.Any(player => player.PlayerId == session.PlayerId))
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        return Results.Ok(RoomSnapshotMapper.ToSnapshot(room, inviteCodes));
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
        return Results.Created($"/api/rooms/{room.RoomId}", new RoomCreatedResponse(
            room.RoomId,
            inviteCode,
            playerId,
            token,
            RoomSnapshotMapper.ToSnapshot(room, inviteCodes)));
    }

    private static async Task<IResult> JoinAsync(
        JoinRoomRequest? request,
        RoomCoordinator coordinator,
        IInviteCodeRegistry inviteCodes,
        IPlayerSessionStore sessions,
        IRoomRealtimeNotifier notifier,
        RoomMutationDeliveryGate mutationGate,
        IGameStateStore gameStates)
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

        return await mutationGate.RunAsync(roomId, async () =>
        {
            if (gameStates.Exists(roomId))
            {
                return Results.Conflict();
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
            var snapshot = RoomSnapshotMapper.ToSnapshot(result.Value!, inviteCodes);
            var player = snapshot.Players.Single(candidate => candidate.PlayerId == playerId);
            await notifier.PublishMemberJoinedAsync(snapshot, player);
            return Results.Ok(new RoomJoinedResponse(playerId, token, snapshot));
        });
    }

    private static async Task<IResult> LeaveAsync(
        Guid roomId,
        HttpRequest request,
        RoomCoordinator coordinator,
        IInviteCodeRegistry inviteCodes,
        IPlayerSessionStore sessions,
        IRoomRealtimeNotifier notifier,
        RoomMutationDeliveryGate mutationGate,
        IGameCoordinator games,
        IGameStateStore gameStates,
        IRoomStore rooms)
    {
        if (!TryGetSession(request, sessions, out var token, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        return await mutationGate.RunAsync(roomId, async () =>
        {
            if (gameStates.Exists(roomId)
                && rooms.TryGet(roomId, out var activeRoom)
                && activeRoom is not null
                && activeRoom.HostPlayerId != session.PlayerId)
            {
                return Results.Conflict();
            }

            var result = await coordinator.LeaveAsync(new LeaveRoomCommand(roomId, session.PlayerId));
            if (!result.IsSuccess)
            {
                return ToError(result.Error!.Code);
            }

            if (result.Value!.RoomWasClosed)
            {
                try
                {
                    await games.RemoveAsync(roomId);
                    await notifier.PublishRoomClosedAsync(roomId);
                }
                finally
                {
                    sessions.RemoveByRoom(roomId);
                    inviteCodes.Remove(roomId);
                }

                return Results.Ok(new { roomWasClosed = true });
            }

            var snapshot = RoomSnapshotMapper.ToSnapshot(result.Value.Room!, inviteCodes);
            try
            {
                await notifier.PublishMemberLeftAsync(snapshot, session.PlayerId);
            }
            finally
            {
                sessions.Remove(token!);
            }

            return Results.Ok(snapshot);
        });
    }

    private static async Task<IResult> SetReadyAsync(
        Guid roomId,
        SetReadyRequest? request,
        HttpRequest httpRequest,
        RoomCoordinator coordinator,
        IInviteCodeRegistry inviteCodes,
        IPlayerSessionStore sessions,
        IRoomRealtimeNotifier notifier,
        RoomMutationDeliveryGate mutationGate)
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

        return await mutationGate.RunAsync(roomId, async () =>
        {
            var result = await coordinator.SetReadyAsync(new SetRoomReadyCommand(roomId, session.PlayerId, request.IsReady));
            if (!result.IsSuccess)
            {
                return ToError(result.Error!.Code);
            }

            var snapshot = RoomSnapshotMapper.ToSnapshot(result.Value!, inviteCodes);
            if (result.Changed)
            {
                await notifier.PublishReadyChangedAsync(snapshot, session.PlayerId, request.IsReady);
            }

            return Results.Ok(snapshot);
        });
    }

    private static async Task<IResult> SetAiConfigurationAsync(
        Guid roomId,
        UpdateRoomAiConfigurationRequest? request,
        HttpRequest httpRequest,
        RoomCoordinator coordinator,
        IInviteCodeRegistry inviteCodes,
        IPlayerSessionStore sessions,
        IRoomRealtimeNotifier notifier,
        RoomMutationDeliveryGate mutationGate)
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

        return await mutationGate.RunAsync(roomId, async () =>
        {
            var result = await coordinator.SetAiConfigurationAsync(
                new SetRoomAiConfigurationCommand(
                    roomId,
                    session.PlayerId,
                    request.Provider,
                    request.Endpoint,
                    request.Model,
                    request.ApiKey));
            if (!result.IsSuccess)
            {
                return ToError(result.Error!.Code);
            }

            var snapshot = RoomSnapshotMapper.ToSnapshot(result.Value!, inviteCodes);
            if (result.Changed)
            {
                await notifier.PublishRoomSnapshotAsync(snapshot);
            }

            return Results.Ok(result.Value!.AiConfiguration);
        });
    }

    private static async Task<IResult> RemoveAiCredentialAsync(
        Guid roomId,
        HttpRequest httpRequest,
        RoomCoordinator coordinator,
        IInviteCodeRegistry inviteCodes,
        IPlayerSessionStore sessions,
        IRoomRealtimeNotifier notifier,
        RoomMutationDeliveryGate mutationGate)
    {
        if (!TryGetSession(httpRequest, sessions, out _, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        return await mutationGate.RunAsync(roomId, async () =>
        {
            var result = await coordinator.RemoveAiCredentialAsync(
                new RemoveRoomAiCredentialCommand(roomId, session.PlayerId));
            if (!result.IsSuccess)
            {
                return ToError(result.Error!.Code);
            }

            var snapshot = RoomSnapshotMapper.ToSnapshot(result.Value!, inviteCodes);
            if (result.Changed)
            {
                await notifier.PublishRoomSnapshotAsync(snapshot);
            }

            return Results.Ok(result.Value!.AiConfiguration);
        });
    }

    private static async Task<IResult> TestAiConnectionAsync(
        Guid roomId,
        HttpRequest httpRequest,
        IRoomStore rooms,
        IPlayerSessionStore sessions,
        IRoomCredentialStore credentials,
        IAiConnectionTester tester,
        IRoomConnectionTestGate testGate,
        CancellationToken cancellationToken)
    {
        if (!TryGetSession(httpRequest, sessions, out _, out var session))
        {
            return Results.Unauthorized();
        }

        if (session!.RoomId != roomId)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        if (!rooms.TryGet(roomId, out var room) || room is null)
        {
            return Results.NotFound();
        }

        var player = room.Players.SingleOrDefault(candidate => candidate.PlayerId == session.PlayerId);
        if (player is null || room.HostPlayerId != session.PlayerId || !player.IsHost)
        {
            return Results.StatusCode((int)HttpStatusCode.Forbidden);
        }

        var configuration = room.AiConfiguration;
        if (!testGate.TryEnter(roomId, out var lease))
        {
            return Results.Conflict(new AiConnectionTestResult(
                false,
                configuration?.Provider,
                configuration?.Model,
                null,
                AiConnectionTestCodes.TestBusy));
        }

        using (lease)
        {
            if (!rooms.TryGet(roomId, out room) || room is null)
            {
                return Results.NotFound();
            }

            configuration = room.AiConfiguration;
            if (configuration is null)
            {
                return Results.Ok(new AiConnectionTestResult(
                    false,
                    null,
                    null,
                    null,
                    AiConnectionTestCodes.ConfigurationMissing));
            }

            if (!credentials.TryGet(roomId, out var credential) || string.IsNullOrWhiteSpace(credential))
            {
                return Results.Ok(new AiConnectionTestResult(
                    false,
                    configuration.Provider,
                    configuration.Model,
                    null,
                    AiConnectionTestCodes.CredentialMissing));
            }

            var result = await tester.TestAsync(configuration, credential, cancellationToken);
            return Results.Ok(result);
        }
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

    private static IResult ToError(RoomErrorCode errorCode) => errorCode switch
    {
        RoomErrorCode.InvalidNickname or RoomErrorCode.InvalidMaxPlayers or RoomErrorCode.InvalidAiConfiguration => Results.BadRequest(),
        RoomErrorCode.RoomNotFound or RoomErrorCode.AiConfigurationNotFound => Results.NotFound(),
        RoomErrorCode.RoomFull or RoomErrorCode.PlayerAlreadyExists or RoomErrorCode.RoomClosed => Results.Conflict(),
        RoomErrorCode.PlayerNotFound or RoomErrorCode.NotMember or RoomErrorCode.NotHost => Results.StatusCode((int)HttpStatusCode.Forbidden),
        _ => Results.StatusCode((int)HttpStatusCode.InternalServerError)
    };

}

public sealed record CreateRoomRequest(string? Nickname, int MaxPlayers);
public sealed record JoinRoomRequest(string? InviteCode, string? Nickname);
public sealed record SetReadyRequest(bool IsReady);
public sealed class UpdateRoomAiConfigurationRequest
{
    public string? Provider { get; init; }

    public string? Endpoint { get; init; }

    public string? Model { get; init; }

    public string? ApiKey { get; init; }
}

public sealed record RoomCreatedResponse(Guid RoomId, string InviteCode, Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
public sealed record RoomJoinedResponse(Guid PlayerId, string PlayerSessionToken, RoomSnapshot Room);
public sealed record RoomSnapshot(
    Guid RoomId,
    string InviteCode,
    Guid HostPlayerId,
    int MaxPlayers,
    string Status,
    long Revision,
    IReadOnlyList<PlayerSnapshot> Players,
    RoomAiConfiguration? AiConfiguration = null);
public sealed record PlayerSnapshot(Guid PlayerId, string Nickname, bool IsHost, bool IsReady, bool IsConnected);
