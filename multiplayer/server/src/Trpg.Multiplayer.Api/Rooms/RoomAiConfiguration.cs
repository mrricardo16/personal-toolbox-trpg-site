namespace Trpg.Multiplayer.Api.Rooms;

public static class RoomAiProviders
{
    public const string DeepSeek = "deepseek";
    public const string OpenAiCompatible = "openai-compatible";
    public const string DeepSeekEndpoint = "https://api.deepseek.com/v1/chat/completions";
}

public sealed record RoomAiConfiguration(
    string Provider,
    string Endpoint,
    string Model,
    bool CredentialPresent);
