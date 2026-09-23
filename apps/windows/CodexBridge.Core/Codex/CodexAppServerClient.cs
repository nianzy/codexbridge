using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexBridge.Core.Codex;

public sealed class CodexAppServerClient : ICodexClient
{
    private readonly ICodexAppServerTransport transport;
    private readonly bool ownsTransport;
    private readonly Action<string>? diagnostics;
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private CodexInitializeResult? initializeResult;
    private bool initialized;
    private bool disposed;
    private CodexConnectionStatus status = CodexConnectionStatus.Disconnected();

    public CodexAppServerClient(
        ICodexAppServerTransport? transport = null,
        CodexExecutableDiscovery? discovery = null,
        TimeSpan? requestTimeout = null,
        Action<string>? diagnostics = null)
    {
        this.transport = transport ?? new CodexAppServerTransport(discovery, requestTimeout, diagnostics: diagnostics);
        ownsTransport = transport is null;
        this.diagnostics = diagnostics;
    }

    public CodexConnectionStatus Status => status;

    public string? ExecutablePath => transport.ExecutablePath;

    public bool IsFaulted => transport.IsFaulted;

    public async Task<CodexProbe> ProbeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            var account = await ReadAccountAsync(cancellationToken);
            var models = await ListModelsAsync(cancellationToken);
            status = CodexConnectionStatus.Connected();
            return new CodexProbe(initializeResult!, account, models);
        }
        catch (CodexExecutableNotFoundException)
        {
            status = CodexConnectionStatus.NotFound();
            throw;
        }
        catch (CodexDisconnectedException)
        {
            status = CodexConnectionStatus.Disconnected();
            throw;
        }
        catch
        {
            status = CodexConnectionStatus.Error();
            throw;
        }
    }

    public async Task<IReadOnlyList<CodexThreadSummary>> ListThreadsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        ReportDiagnostic("thread/list start");
        var pageLimit = Math.Clamp(limit, 1, 100);
        var cursor = (string?)null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        var threads = new List<CodexThreadSummary>();

        do
        {
            var parameters = new JsonObject
            {
                ["limit"] = pageLimit,
                ["sortKey"] = "updated_at",
                ["sortDirection"] = "desc",
                ["useStateDbOnly"] = true,
                ["archived"] = false,
            };
            if (cursor is not null)
            {
                parameters["cursor"] = cursor;
            }

            var result = await transport.RequestAsync("thread/list", parameters, cancellationToken);
            if (result.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var summary = ParseThreadSummary(item);
                    if (summary is not null)
                    {
                        threads.Add(summary);
                    }
                }
            }

            cursor = result.TryGetProperty("nextCursor", out var nextCursor)
                && nextCursor.ValueKind == JsonValueKind.String
                ? nextCursor.GetString()
                : null;
        }
        while (cursor is not null && seenCursors.Add(cursor));

        status = CodexConnectionStatus.Connected();
        var resultThreads = threads
            .GroupBy(thread => thread.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        ReportDiagnostic($"thread/list success; thread count={resultThreads.Length}");
        return resultThreads;
    }

    public async Task<CodexThreadSnapshot> ReadThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(threadId);
        await EnsureInitializedAsync(cancellationToken);
        var result = await transport.RequestAsync(
            "thread/read",
            new JsonObject
            {
                ["threadId"] = threadId,
                ["includeTurns"] = true,
            },
            cancellationToken);
        if (!result.TryGetProperty("thread", out var thread))
        {
            throw new CodexProtocolException("thread/read response did not contain thread.");
        }

        var summary = ParseThreadSummary(thread)
            ?? throw new CodexProtocolException("thread/read response contained an invalid thread.");
        var turns = ParseTurns(thread);
        status = CodexConnectionStatus.Connected();
        return new CodexThreadSnapshot(summary, turns);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        initialized = false;
        if (ownsTransport)
        {
            await transport.DisposeAsync();
        }

        initializationGate.Dispose();
    }

    internal async Task<CodexAccount> ReadAccountAsync(CancellationToken cancellationToken)
    {
        ReportDiagnostic("account/read start");
        var result = await transport.RequestAsync(
            "account/read",
            new JsonObject { ["refreshToken"] = false },
            cancellationToken);
        var account = ParseAccount(result);
        ReportDiagnostic("account/read success");
        return account;
    }

    internal async Task<IReadOnlyList<CodexModelOption>> ListModelsAsync(CancellationToken cancellationToken)
    {
        ReportDiagnostic("model/list start");
        var models = new List<CodexModelOption>();
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var parameters = new JsonObject
            {
                ["includeHidden"] = false,
                ["limit"] = 100,
            };
            if (cursor is not null)
            {
                parameters["cursor"] = cursor;
            }

            var result = await transport.RequestAsync("model/list", parameters, cancellationToken);
            if (result.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    var option = ParseModel(item);
                    if (option is not null)
                    {
                        models.Add(option);
                    }
                }
            }

            cursor = result.TryGetProperty("nextCursor", out var nextCursor)
                && nextCursor.ValueKind == JsonValueKind.String
                ? nextCursor.GetString()
                : null;
        }
        while (cursor is not null && seenCursors.Add(cursor));

        ReportDiagnostic($"model/list success; model count={models.Count}");
        return models;
    }

    public static CodexInitializeResult ParseInitialize(JsonElement result)
    {
        return new CodexInitializeResult(
            GetString(result, "userAgent"),
            GetString(result, "codexHome"),
            GetString(result, "platformFamily"),
            GetString(result, "platformOs"));
    }

    public static CodexAccount ParseAccount(JsonElement result)
    {
        if (!result.TryGetProperty("account", out var account)
            || account.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new CodexAccount(null, null, GetBoolean(result, "requiresOpenaiAuth"));
        }

        return new CodexAccount(
            GetString(account, "type"),
            GetString(account, "planType"),
            GetBoolean(result, "requiresOpenaiAuth"));
    }

    public static CodexModelOption? ParseModel(JsonElement value)
    {
        var id = GetString(value, "id") ?? GetString(value, "model");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var efforts = new List<string>();
        if (value.TryGetProperty("supportedReasoningEfforts", out var effortValues)
            && effortValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var effort in effortValues.EnumerateArray())
            {
                var name = GetString(effort, "reasoningEffort")
                    ?? (effort.ValueKind == JsonValueKind.String ? effort.GetString() : null);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    efforts.Add(name);
                }
            }
        }

        var defaultEffort = GetString(value, "defaultReasoningEffort")
            ?? efforts.FirstOrDefault()
            ?? "medium";
        if (efforts.Count == 0)
        {
            efforts.Add(defaultEffort);
        }

        return new CodexModelOption(
            id,
            GetString(value, "displayName") ?? id,
            GetString(value, "description") ?? string.Empty,
            GetBoolean(value, "isDefault"),
            defaultEffort,
            efforts.AsReadOnly());
    }

    public static CodexThreadSummary? ParseThreadSummary(JsonElement value)
    {
        var id = GetString(value, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var preview = GetString(value, "preview")?.Trim() ?? string.Empty;
        var name = GetString(value, "name")?.Trim();
        var title = !string.IsNullOrWhiteSpace(name)
            ? name!
            : !string.IsNullOrWhiteSpace(preview)
                ? preview[..Math.Min(preview.Length, 80)]
                : "Codex 任务";
        return new CodexThreadSummary(
            id,
            title,
            preview,
            GetString(value, "cwd"),
            GetTimestamp(value, "createdAt"),
            GetTimestamp(value, "updatedAt"),
            GetTimestamp(value, "recencyAt"),
            GetBoolean(value, "archived"),
            GetString(value, "model"));
    }

    public static IReadOnlyList<CodexTurn> ParseTurns(JsonElement thread)
    {
        if (!thread.TryGetProperty("turns", out var turns)
            || turns.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<CodexTurn>();
        }

        var parsed = new List<CodexTurn>();
        var index = 0;
        foreach (var turn in turns.EnumerateArray())
        {
            var turnId = GetString(turn, "id") ?? $"codex-turn-{index}";
            var userText = ExtractUserText(turn);
            if (string.IsNullOrWhiteSpace(userText))
            {
                index++;
                continue;
            }

            var agentText = ExtractAgentText(turn);
            var user = new CodexMessage($"{turnId}-user", userText);
            var agent = string.IsNullOrWhiteSpace(agentText)
                ? null
                : new CodexMessage($"{turnId}-assistant", agentText);
            parsed.Add(new CodexTurn(
                turnId,
                index,
                GetString(turn, "status") ?? "completed",
                user,
                agent));
            index++;
        }

        return parsed;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            try
            {
                await transport.StartAsync(cancellationToken);
                ReportDiagnostic($"selected executable={transport.ExecutablePath ?? "unknown"}");
                ReportDiagnostic("initialize start");
                var result = await transport.RequestAsync(
                    "initialize",
                    new JsonObject
                    {
                        ["clientInfo"] = new JsonObject
                        {
                            ["name"] = "codexbridge-windows",
                            ["title"] = "Codex Bridge",
                            ["version"] = "3.0.0",
                        },
                        ["capabilities"] = new JsonObject
                        {
                            ["experimentalApi"] = false,
                        },
                    },
                    cancellationToken);
                initializeResult = ParseInitialize(result);
                ReportDiagnostic("initialize success");
                ReportDiagnostic($"initialize.codexHome={initializeResult.CodexHome ?? "(null)"}");
                await transport.NotifyAsync("initialized", new JsonObject(), cancellationToken);
                ReportDiagnostic("initialized notification sent");
                initialized = true;
                status = CodexConnectionStatus.Connected();
            }
            catch (CodexExecutableNotFoundException)
            {
                status = CodexConnectionStatus.NotFound();
                throw;
            }
            catch (CodexDisconnectedException)
            {
                status = CodexConnectionStatus.Disconnected();
                throw;
            }
            catch
            {
                status = CodexConnectionStatus.Error();
                throw;
            }
        }
        finally
        {
            initializationGate.Release();
        }
    }

    private static string ExtractUserText(JsonElement turn)
    {
        if (!turn.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var texts = new List<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (!string.Equals(GetString(item, "type"), "userMessage", StringComparison.Ordinal)
                || !item.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var input in content.EnumerateArray())
            {
                if (string.Equals(GetString(input, "type"), "text", StringComparison.Ordinal))
                {
                    var text = GetString(input, "text");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        texts.Add(text);
                    }
                }
            }
        }

        return string.Join("\n", texts).Trim();
    }

    private void ReportDiagnostic(string message)
    {
        try
        {
            diagnostics?.Invoke(message);
        }
        catch
        {
            // Diagnostics must never affect app-server behavior.
        }
    }

    private static string ExtractAgentText(JsonElement turn)
    {
        if (!turn.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var texts = items.EnumerateArray()
            .Where(item => string.Equals(GetString(item, "type"), "agentMessage", StringComparison.Ordinal))
            .Select(item => GetString(item, "text"))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Cast<string>()
            .ToArray();
        return string.Join("\n\n", texts).Trim();
    }

    private static string? GetString(JsonElement value, string propertyName)
    {
        return value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool GetBoolean(JsonElement value, string propertyName)
    {
        return value.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.True;
    }

    private static DateTimeOffset? GetTimestamp(JsonElement value, string propertyName)
    {
        if (!value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Number
            || !property.TryGetInt64(out var seconds))
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}

public static class CodexDraftRenderer
{
    public static string Render(CodexThreadSnapshot snapshot, IEnumerable<string> selectedTurnIds)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(selectedTurnIds);
        var selected = selectedTurnIds.ToHashSet(StringComparer.Ordinal);
        return string.Join(
            "\n\n---\n\n",
            snapshot.Turns
                .Where(turn => selected.Contains(turn.Id))
                .Select(RenderTurn));
    }

    private static string RenderTurn(CodexTurn turn)
    {
        return $"第 {turn.Index + 1} 轮\n\n用户：\n{turn.UserMessage.Text}\n\nCodex：\n{turn.AgentMessage?.Text ?? "（尚无回复）"}";
    }
}
