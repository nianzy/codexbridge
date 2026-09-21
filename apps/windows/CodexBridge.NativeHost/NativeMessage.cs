using System.Text.Json.Serialization;
using CodexBridge.Core.Capture;

namespace CodexBridge.NativeHost;

public sealed record NativeMessage
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("payload")]
    public CapturePayload? Payload { get; init; }
}

public sealed record NativeResponse
{
    [JsonPropertyName("ok")]
    public required bool Ok { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("conversationID")]
    public string? ConversationId { get; init; }

    public static NativeResponse Success(Guid conversationId)
    {
        return new NativeResponse
        {
            Ok = true,
            Error = null,
            ConversationId = conversationId.ToString("D").ToUpperInvariant(),
        };
    }

    public static NativeResponse Failure(string error)
    {
        return new NativeResponse
        {
            Ok = false,
            Error = error,
            ConversationId = null,
        };
    }
}
