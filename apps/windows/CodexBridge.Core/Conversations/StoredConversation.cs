using CodexBridge.Core.Capture;

namespace CodexBridge.Core.Conversations;

public sealed record StoredConversation(
    Guid Id,
    string SourceKind,
    string? SourceConversationId,
    string Title,
    Uri SourceUrl,
    DateTimeOffset CapturedAt,
    string ContentHash,
    CapturePayload Payload,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public int TurnCount => Payload.Turns.Count;
}
