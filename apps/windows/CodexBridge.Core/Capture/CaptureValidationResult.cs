namespace CodexBridge.Core.Capture;

public sealed record CaptureValidationResult(
    CapturePayload Payload,
    Guid ConversationId,
    string ContentHash);
