using System.Text.Json.Serialization;

namespace CodexBridge.Core.Capture;

public sealed record CapturePayload
{
    [JsonPropertyName("schemaVersion")]
    [JsonRequired]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("source")]
    [JsonRequired]
    public required CaptureSource Source { get; init; }

    [JsonPropertyName("capture")]
    [JsonRequired]
    public required CaptureMetadata Capture { get; init; }

    [JsonPropertyName("selection")]
    [JsonRequired]
    public required CaptureSelection Selection { get; init; }

    [JsonPropertyName("turns")]
    [JsonRequired]
    public required List<CapturedTurn> Turns { get; init; }

    [JsonPropertyName("warnings")]
    [JsonRequired]
    public required List<string> Warnings { get; init; }
}

public sealed record CaptureSource
{
    [JsonPropertyName("kind")]
    [JsonRequired]
    public required string Kind { get; init; }

    [JsonPropertyName("url")]
    [JsonRequired]
    public required Uri Url { get; init; }

    [JsonPropertyName("conversationId")]
    public string? ConversationId { get; init; }

    [JsonPropertyName("title")]
    [JsonRequired]
    public required string Title { get; init; }
}

public sealed record CaptureMetadata
{
    [JsonPropertyName("capturedAt")]
    [JsonRequired]
    public required DateTimeOffset CapturedAt { get; init; }

    [JsonPropertyName("scope")]
    [JsonRequired]
    public required string Scope { get; init; }

    [JsonPropertyName("attachmentCount")]
    [JsonRequired]
    public required int AttachmentCount { get; init; }

    [JsonPropertyName("complete")]
    [JsonRequired]
    public required bool Complete { get; init; }
}

public sealed record CaptureSelection
{
    [JsonPropertyName("selectedTurnIds")]
    [JsonRequired]
    public required List<string> SelectedTurnIds { get; init; }
}

public sealed record CapturedTurn
{
    [JsonPropertyName("id")]
    [JsonRequired]
    public required string Id { get; init; }

    [JsonPropertyName("index")]
    [JsonRequired]
    public required int Index { get; init; }

    [JsonPropertyName("user")]
    [JsonRequired]
    public required CapturedMessage User { get; init; }

    [JsonPropertyName("assistant")]
    public CapturedMessage? Assistant { get; init; }

    [JsonPropertyName("complete")]
    [JsonRequired]
    public required bool Complete { get; init; }
}

public sealed record CapturedMessage
{
    [JsonPropertyName("id")]
    [JsonRequired]
    public required string Id { get; init; }

    [JsonPropertyName("idSource")]
    [JsonRequired]
    public required string IdSource { get; init; }

    [JsonPropertyName("text")]
    [JsonRequired]
    public required string Text { get; init; }
}
