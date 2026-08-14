using Trpg.Multiplayer.Api.Rooms;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IRoomStore, InMemoryRoomStore>();
builder.Services.AddSingleton<RoomCoordinator>();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.Run();

public partial class Program;
