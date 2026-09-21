namespace CodexBridge.Core.Capture;

public enum CaptureValidationErrorCode
{
    InvalidPayload,
    UnsupportedVersion,
    UnsupportedSource,
    InvalidHost,
    EmptyConversation,
    Oversized,
    TurnOrder,
    DuplicateMessageId,
    InvalidSelection,
    InconsistentTurn,
}

public sealed class CaptureValidationException : Exception
{
    public CaptureValidationException(CaptureValidationErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public CaptureValidationException(CaptureValidationErrorCode code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public CaptureValidationErrorCode Code { get; }
}
