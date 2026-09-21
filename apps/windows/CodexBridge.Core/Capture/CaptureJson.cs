using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexBridge.Core.Capture;

public static class CaptureJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = false,
    };

    public static CapturePayload Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize<CapturePayload>(utf8Json, SerializerOptions)
            ?? throw new JsonException("Capture payload cannot be null.");
    }

    public static byte[] Serialize(CapturePayload payload)
    {
        return JsonSerializer.SerializeToUtf8Bytes(payload, SerializerOptions);
    }
}
