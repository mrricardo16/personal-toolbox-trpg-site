using Trpg.Multiplayer.Api.Rooms;
using Trpg.Multiplayer.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IRoomStore, InMemoryRoomStore>();
builder.Services.AddSingleton<RoomCoordinator>();
builder.Services.AddSingleton<IPlayerSessionStore, InMemoryPlayerSessionStore>();
builder.Services.AddSingleton<IInviteCodeGenerator, RandomInviteCodeGenerator>();
builder.Services.AddSingleton<IInviteCodeRegistry, InMemoryInviteCodeRegistry>();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));
app.MapRoomEndpoints();

app.Run();

public partial class Program;
