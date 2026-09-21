using System.Text;
using System.Text.Json;
using CodexBridge.Core.Capture;

namespace CodexBridge.NativeHost;

public sealed class NativeRequestHandler
{
    private const string CaptureImportType = "capture.import";
    private readonly CaptureValidator validator;
    private readonly CaptureInbox inbox;

    public NativeRequestHandler(CaptureValidator? validator = null, CaptureInbox? inbox = null)
    {
        this.validator = validator ?? new CaptureValidator();
        this.inbox = inbox ?? new CaptureInbox();
    }

    public NativeResponse Handle(ReadOnlyMemory<byte> requestBytes)
    {
        try
        {
            using var document = JsonDocument.Parse(requestBytes);
            var request = JsonSerializer.Deserialize<NativeMessage>(
                requestBytes.Span,
                CaptureJson.SerializerOptions);

            if (request?.Type != CaptureImportType)
            {
                return NativeResponse.Failure("Unsupported Codex Bridge Native Messaging request.");
            }

            if (!document.RootElement.TryGetProperty("payload", out var payloadElement)
                || payloadElement.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                return NativeResponse.Failure("The capture.import request has no payload.");
            }

            var payloadBytes = Encoding.UTF8.GetBytes(payloadElement.GetRawText());
            var result = validator.Validate(payloadBytes);
            inbox.Store(result.Payload);
            return NativeResponse.Success(result.ConversationId);
        }
        catch (CaptureValidationException exception)
        {
            return NativeResponse.Failure(exception.Message);
        }
        catch (JsonException)
        {
            return NativeResponse.Failure("The Native Messaging request is not valid JSON.");
        }
    }
}
