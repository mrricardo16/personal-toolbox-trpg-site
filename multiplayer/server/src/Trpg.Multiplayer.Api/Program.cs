using Trpg.Multiplayer.Api.Gameplay;
using Trpg.Multiplayer.Api.Rooms;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api;
using Trpg.Multiplayer.Api.Ai;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddHttpClient(AiConnectionTester.HttpClientName, client =>
{
    client.Timeout = AiConnectionTester.ConnectionTimeout;
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    AllowAutoRedirect = false
});
builder.Services.AddSingleton<IRoomStore, InMemoryRoomStore>();
builder.Services.AddSingleton<IGameStateStore, InMemoryGameStateStore>();
builder.Services.AddSingleton<IDiceRoller, SecureDiceRoller>();
builder.Services.AddSingleton<ICheckResolutionEngine, CocCheckResolutionEngine>();
builder.Services.AddSingleton<IRoomCredentialStore, InMemoryRoomCredentialStore>();
builder.Services.AddSingleton<IHostAddressResolver, DnsHostAddressResolver>();
builder.Services.AddSingleton<IAiEndpointPolicy, AiEndpointPolicy>();
builder.Services.AddSingleton<IAiConnectionTester, AiConnectionTester>();
builder.Services.AddSingleton<IRoomConnectionTestGate, InMemoryRoomConnectionTestGate>();
builder.Services.AddSingleton<RoomCoordinator>();
builder.Services.AddSingleton<IGameCoordinator, GameCoordinator>();
builder.Services.AddSingleton<RoomMutationDeliveryGate>();
builder.Services.AddSingleton<IPlayerSessionStore, InMemoryPlayerSessionStore>();
builder.Services.AddSingleton<IInviteCodeGenerator, RandomInviteCodeGenerator>();
builder.Services.AddSingleton<IInviteCodeRegistry, InMemoryInviteCodeRegistry>();
builder.Services.AddSingleton<IPlayerConnectionRegistry, InMemoryPlayerConnectionRegistry>();
builder.Services.AddSingleton<IRoomRealtimeNotifier, SignalRRoomRealtimeNotifier>();
builder.Services.AddSingleton<IGameRealtimeNotifier, SignalRGameRealtimeNotifier>();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));
app.MapRoomEndpoints();
app.MapGameEndpoints();
app.MapHub<RoomHub>("/hubs/room");

app.Run();

public partial class Program;
