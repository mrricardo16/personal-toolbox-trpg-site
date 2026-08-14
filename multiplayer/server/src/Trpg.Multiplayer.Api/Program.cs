using Trpg.Multiplayer.Api.Rooms;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IRoomStore, InMemoryRoomStore>();

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new { status = "ok" }));

app.Run();

public partial class Program;
