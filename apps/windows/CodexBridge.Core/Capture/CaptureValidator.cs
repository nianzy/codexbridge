using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CodexBridge.Core.Capture;

public sealed class CaptureValidator
{
    public const int MaximumPayloadBytes = 2 * 1_024 * 1_024;

    public CaptureValidationResult Validate(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length > MaximumPayloadBytes)
        {
            throw Error(
                CaptureValidationErrorCode.Oversized,
                $"Capture payload exceeds the {MaximumPayloadBytes}-byte limit.");
        }

        CapturePayload payload;
        try
        {
            payload = CaptureJson.Deserialize(utf8Json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new CaptureValidationException(
                CaptureValidationErrorCode.InvalidPayload,
                "Capture payload is not valid JSON.",
                exception);
        }

        return Validate(payload);
    }

    public CaptureValidationResult Validate(CapturePayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.SchemaVersion != 1)
        {
            throw Error(CaptureValidationErrorCode.UnsupportedVersion, "Unsupported capture schema version.");
        }

        if (payload.Source is null || payload.Source.Kind != "chatgpt-web")
        {
            throw Error(CaptureValidationErrorCode.UnsupportedSource, "Unsupported capture source.");
        }

        if (payload.Source.Url is null
            || !payload.Source.Url.IsAbsoluteUri
            || !string.Equals(payload.Source.Url.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase))
        {
            throw Error(CaptureValidationErrorCode.InvalidHost, "Capture URL must belong to chatgpt.com.");
        }

        if (string.IsNullOrEmpty(payload.Source.Title) || payload.Source.Title.Length > 500)
        {
            throw Error(CaptureValidationErrorCode.InvalidPayload, "Capture source title is invalid.");
        }

        if (payload.Capture is null
            || payload.Capture.Scope != "rendered-current-page"
            || payload.Capture.AttachmentCount < 0)
        {
            throw Error(CaptureValidationErrorCode.InvalidPayload, "Capture metadata is invalid.");
        }

        if (payload.Warnings is null || payload.Warnings.Any(warning => warning is null))
        {
            throw Error(CaptureValidationErrorCode.InvalidPayload, "Capture warnings are invalid.");
        }

        if (payload.Turns is null || payload.Turns.Count == 0)
        {
            throw Error(CaptureValidationErrorCode.EmptyConversation, "Capture contains no conversation turns.");
        }

        ValidateTurnOrder(payload.Turns);
        ValidateMessages(payload.Turns);
        ValidateSelection(payload.Selection, payload.Turns);

        var canonicalTurns = SerializeCanonicalTurns(payload.Turns);
        var contentHash = Convert.ToHexString(SHA256.HashData(canonicalTurns)).ToLowerInvariant();
        var conversationId = payload.Source.ConversationId?.Trim();
        var identitySeed = string.IsNullOrEmpty(conversationId)
            ? $"chatgpt-web-content-{contentHash}"
            : $"chatgpt-web-{conversationId}";

        return new CaptureValidationResult(
            payload,
            CreateStableGuid(identitySeed),
            contentHash);
    }

    private static void ValidateTurnOrder(IReadOnlyList<CapturedTurn> turns)
    {
        var indexes = new HashSet<int>();
        var previousIndex = int.MinValue;
        foreach (var turn in turns)
        {
            if (turn is null
                || string.IsNullOrEmpty(turn.Id)
                || turn.Index < 0
                || turn.Index < previousIndex
                || !indexes.Add(turn.Index))
            {
                throw Error(CaptureValidationErrorCode.TurnOrder, "Capture turn order is invalid.");
            }

            previousIndex = turn.Index;
        }
    }

    private static void ValidateMessages(IReadOnlyList<CapturedTurn> turns)
    {
        var messageIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var turn in turns)
        {
            if (turn.User is null
                || string.IsNullOrEmpty(turn.User.Id)
                || turn.User.IdSource is null
                || turn.User.IdSource is not ("dom" or "dom-order")
                || string.IsNullOrEmpty(turn.User.Text))
            {
                throw Error(CaptureValidationErrorCode.InvalidPayload, "Capture contains an invalid user message.");
            }

            if (!messageIds.Add(turn.User.Id))
            {
                throw Error(
                    CaptureValidationErrorCode.DuplicateMessageId,
                    $"Duplicate message ID: {turn.User.Id}");
            }

            if (string.IsNullOrWhiteSpace(turn.User.Text))
            {
                throw Error(CaptureValidationErrorCode.InconsistentTurn, $"Turn {turn.Id} has no user text.");
            }

            if (turn.Assistant is not null)
            {
                if (string.IsNullOrEmpty(turn.Assistant.Id)
                    || turn.Assistant.IdSource is null
                    || turn.Assistant.IdSource is not ("dom" or "dom-order")
                    || string.IsNullOrEmpty(turn.Assistant.Text))
                {
                    throw Error(CaptureValidationErrorCode.InvalidPayload, "Capture contains an invalid assistant message.");
                }

                if (!messageIds.Add(turn.Assistant.Id))
                {
                    throw Error(
                        CaptureValidationErrorCode.DuplicateMessageId,
                        $"Duplicate message ID: {turn.Assistant.Id}");
                }
            }

            if (turn.Complete && turn.Assistant is null)
            {
                throw Error(
                    CaptureValidationErrorCode.InconsistentTurn,
                    $"Turn {turn.Id} is marked complete without an assistant message.");
            }
        }
    }

    private static void ValidateSelection(CaptureSelection selection, IReadOnlyList<CapturedTurn> turns)
    {
        if (selection?.SelectedTurnIds is null
            || selection.SelectedTurnIds.Count != turns.Count
            || selection.SelectedTurnIds.Distinct(StringComparer.Ordinal).Count() != selection.SelectedTurnIds.Count
            || !selection.SelectedTurnIds.SequenceEqual(turns.Select(turn => turn.Id), StringComparer.Ordinal))
        {
            throw Error(
                CaptureValidationErrorCode.InvalidSelection,
                "Selected turn IDs must exactly match the payload turns.");
        }
    }

    private static byte[] SerializeCanonicalTurns(IReadOnlyList<CapturedTurn> turns)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   stream,
                   new JsonWriterOptions
                   {
                       Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                       Indented = false,
                   }))
        {
            writer.WriteStartArray();
            foreach (var turn in turns)
            {
                writer.WriteStartObject();
                if (turn.Assistant is not null)
                {
                    writer.WritePropertyName("assistant");
                    WriteMessage(writer, turn.Assistant);
                }

                writer.WriteBoolean("complete", turn.Complete);
                writer.WriteString("id", turn.Id);
                writer.WriteNumber("index", turn.Index);
                writer.WritePropertyName("user");
                WriteMessage(writer, turn.User);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return stream.ToArray();
    }

    private static void WriteMessage(Utf8JsonWriter writer, CapturedMessage message)
    {
        writer.WriteStartObject();
        writer.WriteString("id", message.Id);
        writer.WriteString("idSource", message.IdSource);
        writer.WriteString("text", message.Text);
        writer.WriteEndObject();
    }

    private static Guid CreateStableGuid(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var hex = Convert.ToHexString(hash.AsSpan(0, 16));
        var valueWithSeparators = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
        return Guid.ParseExact(valueWithSeparators, "D");
    }

    private static CaptureValidationException Error(CaptureValidationErrorCode code, string message)
    {
        return new CaptureValidationException(code, message);
    }
}
