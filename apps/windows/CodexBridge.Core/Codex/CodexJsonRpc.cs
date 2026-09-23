using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexBridge.Core.Codex;

public static class CodexJsonRpc
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static string SerializeRequest(long id, string method, JsonObject? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new JsonObject(),
        };
        return request.ToJsonString(SerializerOptions);
    }

    public static string SerializeNotification(string method, JsonObject? parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        var notification = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters ?? new JsonObject(),
        };
        return notification.ToJsonString(SerializerOptions);
    }

    public static bool TryGetResponse(JsonElement message, out long id, out JsonElement? result, out JsonElement? error)
    {
        id = 0;
        result = null;
        error = null;
        if (!message.TryGetProperty("id", out var idElement) || !TryReadId(idElement, out id))
        {
            return false;
        }

        if (message.TryGetProperty("error", out var errorElement))
        {
            error = errorElement.Clone();
            return true;
        }

        if (!message.TryGetProperty("result", out var resultElement))
        {
            return false;
        }

        result = resultElement.Clone();
        return true;
    }

    public static bool TryReadId(JsonElement value, out long id)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out id))
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out id))
        {
            return true;
        }

        id = 0;
        return false;
    }
}

