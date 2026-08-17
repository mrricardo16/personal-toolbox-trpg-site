using Trpg.Multiplayer.Api.Rooms;
using Trpg.Multiplayer.Api.Realtime;
using Trpg.Multiplayer.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRoomStore, InMemoryRoomStore>();
builder.Services.AddSingleton<RoomCoordinator>();
builder.Services.AddSingleton<RoomMutationDeliveryGate>();
builder.Services.AddSingleton<IPlayerSessionStore, InMemoryPlayerSessionStore>();
builder.Services.AddSingleton<IInviteCodeGenerator, RandomInviteCodeGenerator>();
builder.Services.AddSingleton<IInviteCodeRegistry, InMemoryInviteCodeRegistry>();
builder.Services.AddSingleton<IPlayerConnectionRegistry, InMemoryPlayerConnectionRegistry>();
builder.Services.AddSingleton<IRoomRealtimeNotifier, SignalRRoomRealtimeNotifier>();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));
app.MapRoomEndpoints();
app.MapHub<RoomHub>("/hubs/room");

app.Run();

public partial class Program;
