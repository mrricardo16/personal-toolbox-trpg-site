namespace Trpg.Multiplayer.Api.Realtime;

public static class RoomGroupNames
{
    public static string For(Guid roomId) => $"room:{roomId}";
}
