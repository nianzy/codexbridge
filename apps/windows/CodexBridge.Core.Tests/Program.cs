using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexBridge.Core.Capture;
using CodexBridge.Core.Codex;
using CodexBridge.NativeHost;

var tests = new (string Name, Func<Task> Test)[]
{
    ("synthetic fixture deserializes", Sync(SyntheticFixtureDeserializes)),
    ("valid payload validates", Sync(ValidPayloadValidates)),
    ("schema version mismatch fails", Sync(SchemaVersionMismatchFails)),
    ("duplicate message ID fails", Sync(DuplicateMessageIdFails)),
    ("selection mismatch fails", Sync(SelectionMismatchFails)),
    ("oversized payload fails", Sync(OversizedPayloadFails)),
    ("Native Messaging framing round-trips", Sync(NativeMessagingFramingRoundTrips)),
    ("capture.import stores a valid payload", Sync(CaptureImportStoresValidPayload)),
    ("unsupported Native Messaging request fails", Sync(UnsupportedNativeRequestFails)),
    ("Codex executable discovery rejects extensionless launcher", Sync(CodexExecutableDiscoveryRejectsExtensionlessLauncher)),
    ("Codex executable discovery prefers a native binary", Sync(CodexExecutableDiscoveryFindsNativeBinary)),
    ("Codex executable discovery accepts command script", Sync(CodexExecutableDiscoveryAcceptsCommandScript)),
    ("Codex executable discovery preserves explicit override", Sync(CodexExecutableDiscoveryPreservesOverride)),
    ("Codex selected launcher reaches transport unchanged", Sync(CodexSelectedLauncherReachesTransportUnchanged)),
    ("Codex process stdio uses strict UTF-8", Sync(CodexProcessStdioUsesStrictUtf8)),
    ("Codex JSONL request serialization", Sync(CodexJsonRpcRequestSerialization)),
    ("Codex JSONL response correlation", Sync(CodexJsonRpcResponseCorrelation)),
    ("Codex initialize account model and thread parsing", Sync(CodexProtocolParsing)),
    ("Codex reasoning items are ignored", Sync(CodexReasoningItemsAreIgnored)),
    ("Codex draft renderer uses visible turns", Sync(CodexDraftRendererWorks)),
    ("Codex cancellation is propagated", CodexCancellationIsPropagatedAsync),
    ("Codex process exit becomes disconnected", CodexProcessExitBecomesDisconnectedAsync),
    ("Codex stdout valid line is parsed at protocol boundary", Sync(CodexStdoutValidLineIsParsed)),
    ("Codex stdout invalid line faults transport", Sync(CodexStdoutInvalidLineFaultsTransport)),
    ("Codex fault rejects new requests immediately", CodexFaultRejectsNewRequestsImmediatelyAsync),
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"Tests: {tests.Length - failures} passed, {failures} failed.");
return failures == 0 ? 0 : 1;

static CapturePayload LoadFixture()
{
    var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic-capture-v1.json");
    return CaptureJson.Deserialize(File.ReadAllBytes(path));
}

static void SyntheticFixtureDeserializes()
{
    var payload = LoadFixture();
    Assert(payload.SchemaVersion == 1, "schemaVersion should be 1");
    Assert(payload.Source.Kind == "chatgpt-web", "fixture source kind mismatch");
    Assert(payload.Turns.Count == 1, "fixture should contain one turn");
}

static void ValidPayloadValidates()
{
    var result = new CaptureValidator().Validate(File.ReadAllBytes(
        Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic-capture-v1.json")));
    Assert(result.ContentHash.Length == 64, "content hash must be SHA-256 hex");
    Assert(result.ConversationId != Guid.Empty, "conversation ID must be generated");
}

static void SchemaVersionMismatchFails()
{
    var payload = LoadFixture() with { SchemaVersion = 2 };
    AssertValidationError(payload, CaptureValidationErrorCode.UnsupportedVersion);
}

static void DuplicateMessageIdFails()
{
    var payload = LoadFixture();
    var turn = payload.Turns[0];
    var duplicateAssistant = turn.Assistant! with { Id = turn.User.Id };
    var turns = payload.Turns.ToList();
    turns[0] = turn with { Assistant = duplicateAssistant };
    AssertValidationError(payload with { Turns = turns }, CaptureValidationErrorCode.DuplicateMessageId);
}

static void SelectionMismatchFails()
{
    var payload = LoadFixture() with
    {
        Selection = new CaptureSelection { SelectedTurnIds = new List<string> { "missing-turn" } },
    };
    AssertValidationError(payload, CaptureValidationErrorCode.InvalidSelection);
}

static void OversizedPayloadFails()
{
    var bytes = new byte[CaptureValidator.MaximumPayloadBytes + 1];
    AssertThrows<CaptureValidationException>(
        () => new CaptureValidator().Validate(bytes),
        exception => exception.Code == CaptureValidationErrorCode.Oversized);
}

static void NativeMessagingFramingRoundTrips()
{
    var expected = NativeResponse.Success(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"));
    using var stream = new MemoryStream();
    NativeMessagingProtocol.WriteMessage(stream, expected);
    var bytes = stream.ToArray();
    var declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
    Assert(declaredLength == bytes.Length - 4, "Native Messaging length prefix mismatch");

    stream.Position = 0;
    var decodedBytes = NativeMessagingProtocol.ReadMessage(stream);
    Assert(decodedBytes is not null, "framing decoder returned no payload");
    var decoded = JsonSerializer.Deserialize<NativeResponse>(
        decodedBytes!,
        CaptureJson.SerializerOptions);
    Assert(decoded?.Ok == expected.Ok, "framing response ok mismatch");
    Assert(decoded?.ConversationId == expected.ConversationId, "framing conversation ID mismatch");
}

static void UnsupportedNativeRequestFails()
{
    var handler = new NativeRequestHandler(
        inbox: new CaptureInbox(Path.Combine(Path.GetTempPath(), $"codexbridge-test-{Guid.NewGuid():D}")));
    var request = Encoding.UTF8.GetBytes("{\"type\":\"not-supported\",\"payload\":null}");
    var response = handler.Handle(request);
    Assert(!response.Ok, "unsupported request should fail");
    Assert(response.ConversationId is null, "failed request must not return conversation ID");
}

static void CaptureImportStoresValidPayload()
{
    var directory = Path.Combine(Path.GetTempPath(), $"codexbridge-test-{Guid.NewGuid():D}");
    try
    {
        var handler = new NativeRequestHandler(inbox: new CaptureInbox(directory));
        var request = JsonSerializer.SerializeToUtf8Bytes(
            new NativeMessage { Type = "capture.import", Payload = LoadFixture() },
            CaptureJson.SerializerOptions);

        var response = handler.Handle(request);
        Assert(response.Ok, "capture.import should succeed for the fixture");
        Assert(response.ConversationId is not null, "successful import must return a conversation ID");

        var storedFiles = Directory.GetFiles(directory, "*.json");
        Assert(storedFiles.Length == 1, "capture.import should write one inbox file");
        var stored = new CaptureValidator().Validate(File.ReadAllBytes(storedFiles[0]));
        Assert(stored.ConversationId.ToString("D").ToUpperInvariant() == response.ConversationId,
            "inbox payload and response conversation IDs must match");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void AssertValidationError(CapturePayload payload, CaptureValidationErrorCode expected)
{
    AssertThrows<CaptureValidationException>(
        () => new CaptureValidator().Validate(payload),
        exception => exception.Code == expected);
}

static void AssertThrows<TException>(Action action, Func<TException, bool> predicate)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception) when (predicate(exception))
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static Func<Task> Sync(Action action)
{
    return () =>
    {
        action();
        return Task.CompletedTask;
    };
}

static void CodexExecutableDiscoveryFindsNativeBinary()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-discovery-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var shimDirectory = Path.Combine(root, "shim");
        var nativeDirectory = Path.Combine(root, "native");
        Directory.CreateDirectory(shimDirectory);
        Directory.CreateDirectory(nativeDirectory);
        File.WriteAllBytes(Path.Combine(shimDirectory, "codex"), Array.Empty<byte>());
        var shim = Path.Combine(shimDirectory, "codex.cmd");
        var powerShellShim = Path.Combine(shimDirectory, "codex.ps1");
        var native = Path.Combine(nativeDirectory, "codex.exe");
        File.WriteAllText(shim, "@echo off");
        File.WriteAllText(powerShellShim, "param()");
        File.WriteAllBytes(native, Array.Empty<byte>());

        var found = CodexExecutableDiscovery.FindFromPathEntries(new[] { shimDirectory, nativeDirectory });
        Assert(found is not null, "Codex executable was not discovered");
        Assert(found!.Path == native, "native Codex binary should win over a shim");
        Assert(found.LauncherKind == CodexLauncherKind.NativeExecutable, "native Codex binary was marked as a shim");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void CodexExecutableDiscoveryRejectsExtensionlessLauncher()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-discovery-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        File.WriteAllBytes(Path.Combine(root, "codex"), Array.Empty<byte>());
        var found = CodexExecutableDiscovery.FindFromPathEntries(new[] { root });
        Assert(found is null, "extensionless Codex launcher must be rejected on Windows");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CodexExecutableDiscoveryAcceptsCommandScript()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-discovery-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var commandScript = Path.Combine(root, "codex.cmd");
        File.WriteAllText(commandScript, "@echo off");
        var found = CodexExecutableDiscovery.FindFromPathEntries(new[] { root });
        Assert(found?.Path == commandScript, "codex.cmd was not discovered");
        Assert(found?.LauncherKind == CodexLauncherKind.CommandScript, "codex.cmd launcher kind mismatch");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CodexExecutableDiscoveryPreservesOverride()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-discovery-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    var original = Environment.GetEnvironmentVariable(CodexExecutableDiscovery.OverrideEnvironmentVariable);
    try
    {
        var explicitExecutable = Path.Combine(root, "chosen-codex.exe");
        File.WriteAllBytes(explicitExecutable, Array.Empty<byte>());
        Environment.SetEnvironmentVariable(CodexExecutableDiscovery.OverrideEnvironmentVariable, explicitExecutable);

        var found = new CodexExecutableDiscovery().Find();
        Assert(found?.Path == explicitExecutable, "explicit Codex override path was not preserved");
        Assert(found?.LauncherKind == CodexLauncherKind.NativeExecutable, "explicit Codex override launcher kind mismatch");
    }
    finally
    {
        Environment.SetEnvironmentVariable(CodexExecutableDiscovery.OverrideEnvironmentVariable, original);
        Directory.Delete(root, recursive: true);
    }
}

static void CodexSelectedLauncherReachesTransportUnchanged()
{
    var root = Path.Combine(Path.GetTempPath(), $"codexbridge-discovery-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        var nativePath = Path.Combine(root, "codex.exe");
        File.WriteAllBytes(nativePath, Array.Empty<byte>());
        var native = CodexExecutableDiscovery.FindFromPathEntries(new[] { root });
        Assert(native is not null, "native launcher was not selected");
        var nativeStartInfo = CodexAppServerTransport.CreateStartInfo(native!);
        Assert(nativeStartInfo.FileName == nativePath, "native launcher path changed before transport startup");

        File.Delete(nativePath);
        var commandPath = Path.Combine(root, "codex.cmd");
        File.WriteAllText(commandPath, "@echo off");
        var command = CodexExecutableDiscovery.FindFromPathEntries(new[] { root });
        Assert(command is not null, "command launcher was not selected");
        var commandStartInfo = CodexAppServerTransport.CreateStartInfo(command!);
        Assert(commandStartInfo.Arguments.Contains(commandPath, StringComparison.Ordinal), "command launcher path changed before transport startup");
        Assert(commandStartInfo.Arguments.StartsWith("/d /s /c", StringComparison.Ordinal), "command launcher must use cmd.exe /d /s /c");

        File.Delete(commandPath);
        var powerShellPath = Path.Combine(root, "codex.ps1");
        File.WriteAllText(powerShellPath, "param()");
        var powerShell = CodexExecutableDiscovery.FindFromPathEntries(new[] { root });
        Assert(powerShell is not null, "PowerShell launcher was not selected");
        var powerShellStartInfo = CodexAppServerTransport.CreateStartInfo(powerShell!);
        Assert(powerShellStartInfo.FileName == "powershell.exe", "PowerShell launcher must not be direct-executed");
        Assert(powerShellStartInfo.ArgumentList.Contains(powerShellPath), "PowerShell launcher path changed before transport startup");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void CodexProcessStdioUsesStrictUtf8()
{
    var executable = new CodexExecutable("C:\\fixture\\codex.exe", CodexLauncherKind.NativeExecutable);
    var startInfo = CodexAppServerTransport.CreateStartInfo(executable);
    var encodings = new[]
    {
        startInfo.StandardInputEncoding,
        startInfo.StandardOutputEncoding,
        startInfo.StandardErrorEncoding,
    };

    foreach (var encoding in encodings)
    {
        Assert(encoding is not null, "redirected Codex stdio encoding must be explicit");
        Assert(encoding!.WebName == "utf-8", "redirected Codex stdio encoding must be UTF-8");
        Assert(encoding.GetPreamble().Length == 0, "redirected Codex stdio encoding must not emit a BOM");
        Assert(encoding.EncoderFallback is EncoderExceptionFallback, "UTF-8 encoder must throw on invalid input");
        Assert(encoding.DecoderFallback is DecoderExceptionFallback, "UTF-8 decoder must throw on invalid bytes");
        AssertThrows<DecoderFallbackException>(
            () => encoding.GetString(new byte[] { 0xC3, 0x28 }),
            _ => true);
    }
}

static void CodexJsonRpcRequestSerialization()
{
    var line = CodexJsonRpc.SerializeRequest(
        7,
        "thread/list",
        new JsonObject { ["limit"] = 100, ["useStateDbOnly"] = true });
    using var document = JsonDocument.Parse(line);
    var root = document.RootElement;
    Assert(root.GetProperty("jsonrpc").GetString() == "2.0", "jsonrpc version mismatch");
    Assert(root.GetProperty("id").GetInt64() == 7, "request id mismatch");
    Assert(root.GetProperty("method").GetString() == "thread/list", "request method mismatch");
    Assert(root.GetProperty("params").GetProperty("useStateDbOnly").GetBoolean(), "request params mismatch");
}

static void CodexJsonRpcResponseCorrelation()
{
    using var document = JsonDocument.Parse("{\"jsonrpc\":\"2.0\",\"id\":\"7\",\"result\":{\"ok\":true}}");
    var correlated = CodexJsonRpc.TryGetResponse(document.RootElement, out var id, out var result, out var error);
    Assert(correlated, "response was not recognized");
    Assert(id == 7, "string response id was not correlated");
    Assert(result?.GetProperty("ok").GetBoolean() == true, "response result mismatch");
    Assert(error is null, "successful response unexpectedly contained an error");
}

static void CodexProtocolParsing()
{
    using var initializeDocument = JsonDocument.Parse("{\"userAgent\":\"codex-cli 0.153.4\",\"codexHome\":\"C:/Users/test/.codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\"}");
    var initialize = CodexAppServerClient.ParseInitialize(initializeDocument.RootElement);
    Assert(initialize.UserAgent == "codex-cli 0.153.4", "initialize userAgent mismatch");

    using var accountDocument = JsonDocument.Parse("{\"account\":{\"type\":\"chatgpt\",\"email\":\"hidden@example.test\",\"planType\":\"plus\"},\"requiresOpenaiAuth\":false}");
    var account = CodexAppServerClient.ParseAccount(accountDocument.RootElement);
    Assert(account.Type == "chatgpt" && account.PlanType == "plus", "account fields mismatch");
    Assert(account.DisplayLabel == "ChatGPT (plus)", "account display label mismatch");

    using var modelDocument = JsonDocument.Parse("{\"id\":\"gpt-test\",\"model\":\"gpt-test\",\"displayName\":\"Test\",\"description\":\"fixture\",\"isDefault\":true,\"defaultReasoningEffort\":\"medium\",\"supportedReasoningEfforts\":[{\"reasoningEffort\":\"low\"},{\"reasoningEffort\":\"medium\"}]}");
    var model = CodexAppServerClient.ParseModel(modelDocument.RootElement);
    Assert(model is not null && model.SupportedReasoningEfforts.Count == 2, "model fields mismatch");

    using var threadDocument = JsonDocument.Parse(FakeCodexTransport.ThreadResponseJson);
    var summary = CodexAppServerClient.ParseThreadSummary(threadDocument.RootElement.GetProperty("thread"));
    Assert(summary?.Title == "Fixture Codex thread", "thread title mismatch");
    Assert(summary?.Cwd == "C:/work", "thread cwd mismatch");
}

static void CodexReasoningItemsAreIgnored()
{
    using var document = JsonDocument.Parse(FakeCodexTransport.ThreadResponseJson);
    var turns = CodexAppServerClient.ParseTurns(document.RootElement.GetProperty("thread"));
    Assert(turns.Count == 1, "expected one visible turn");
    Assert(turns[0].AgentMessage?.Text == "Visible answer", "agent message was not parsed");
    Assert(!turns[0].AgentMessage!.Text.Contains("internal", StringComparison.OrdinalIgnoreCase), "reasoning leaked into agent text");
}

static void CodexDraftRendererWorks()
{
    using var document = JsonDocument.Parse(FakeCodexTransport.ThreadResponseJson);
    var thread = document.RootElement.GetProperty("thread");
    var summary = CodexAppServerClient.ParseThreadSummary(thread)!;
    var snapshot = new CodexThreadSnapshot(summary, CodexAppServerClient.ParseTurns(thread));
    var rendered = CodexDraftRenderer.Render(snapshot, new[] { "turn-1" });
    Assert(rendered == "第 1 轮\n\n用户：\nVisible question\n\nCodex：\nVisible answer", "Codex draft format mismatch");
}

static async Task CodexCancellationIsPropagatedAsync()
{
    await using var transport = new FakeCodexTransport { DelayThreadRead = true };
    await using var client = new CodexAppServerClient(transport);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));
    await AssertThrowsAsync<OperationCanceledException>(
        () => client.ReadThreadAsync("thread-1", cancellation.Token));
}

static async Task CodexProcessExitBecomesDisconnectedAsync()
{
    await using var transport = new FakeCodexTransport { FailInitialize = true };
    await using var client = new CodexAppServerClient(transport);
    await AssertThrowsAsync<CodexDisconnectedException>(() => client.ProbeAsync());
    Assert(client.Status.State == CodexConnectionState.Disconnected, "process exit was not exposed as disconnected");
}

static void CodexStdoutValidLineIsParsed()
{
    var diagnostics = new List<string>();
    var transport = new CodexAppServerTransport(diagnostics: diagnostics.Add);
    try
    {
        Assert(transport.ProcessStdoutLineForTesting("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{}}"), "valid JSON line was not parsed");
        Assert(!transport.IsFaulted, "valid JSON line faulted transport");
        Assert(diagnostics.Any(line => line.Contains("parse=true", StringComparison.Ordinal)), "valid line boundary diagnostic missing");
    }
    finally
    {
        transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

static void CodexStdoutInvalidLineFaultsTransport()
{
    var diagnostics = new List<string>();
    var transport = new CodexAppServerTransport(diagnostics: diagnostics.Add);
    try
    {
        Assert(!transport.ProcessStdoutLineForTesting("{\"jsonrpc\":\"2.0\",\"result\":{\"text\":\"bad e\"}}e"), "invalid JSON line unexpectedly parsed");
        Assert(transport.IsFaulted, "invalid JSON line did not fault transport");
        Assert(diagnostics.Any(line => line.Contains("parse=false", StringComparison.Ordinal)), "invalid line boundary diagnostic missing");
        Assert(diagnostics.All(line => !line.Contains("bad e", StringComparison.Ordinal)), "raw JSON leaked into diagnostics");
    }
    finally
    {
        transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

static async Task CodexFaultRejectsNewRequestsImmediatelyAsync()
{
    await using var transport = new CodexAppServerTransport();
    transport.MarkFaultForTesting(new CodexProtocolException("fixture protocol fault"));
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));
    await AssertThrowsAsync<CodexTransportFaultedException>(
        () => transport.RequestAsync("account/read", new JsonObject(), cancellation.Token));
}

static async Task AssertThrowsAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class FakeCodexTransport : ICodexAppServerTransport
{
    public const string ThreadResponseJson = """
        {
          "thread": {
            "id": "thread-1",
            "name": "Fixture Codex thread",
            "preview": "Visible question",
            "cwd": "C:/work",
            "createdAt": 1700000000,
            "updatedAt": 1700000100,
            "recencyAt": 1700000100,
            "turns": [
              {
                "id": "turn-1",
                "status": "completed",
                "items": [
                  { "id": "user-1", "type": "userMessage", "content": [ { "type": "text", "text": "Visible question" } ] },
                  { "id": "reasoning-1", "type": "reasoning", "summary": [ { "type": "summaryText", "text": "internal reasoning" } ] },
                  { "id": "agent-1", "type": "agentMessage", "text": "Visible answer" }
                ]
              }
            ]
          }
        }
        """;

    public bool DelayThreadRead { get; init; }

    public bool FailInitialize { get; init; }

    public bool IsRunning { get; private set; }

    public bool IsFaulted { get; private set; }

    public string? ExecutablePath => "fake-codex.exe";

    public string? LastError { get; private set; }

    public IReadOnlyList<string> StderrLines => Array.Empty<string>();

    public List<string> Calls { get; } = new();

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        IsRunning = true;
        return Task.CompletedTask;
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        JsonObject? parameters,
        CancellationToken cancellationToken = default)
    {
        Calls.Add(method);
        if (method == "initialize" && FailInitialize)
        {
            IsRunning = false;
            LastError = "fake process exited";
            throw new CodexDisconnectedException("fake process exited");
        }

        if (method == "thread/read" && DelayThreadRead)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        var json = method switch
        {
            "initialize" => "{\"userAgent\":\"codex-cli 0.153.4\",\"codexHome\":\"C:/Users/test/.codex\",\"platformFamily\":\"windows\",\"platformOs\":\"windows\"}",
            "account/read" => "{\"account\":null,\"requiresOpenaiAuth\":true}",
            "model/list" => "{\"data\":[{\"id\":\"gpt-test\",\"model\":\"gpt-test\",\"displayName\":\"Test\",\"description\":\"fixture\",\"isDefault\":true,\"defaultReasoningEffort\":\"medium\",\"supportedReasoningEfforts\":[{\"reasoningEffort\":\"medium\"}]}]}",
            "thread/list" => "{\"data\":[{\"id\":\"thread-1\",\"name\":\"Fixture Codex thread\",\"preview\":\"Visible question\",\"cwd\":\"C:/work\",\"createdAt\":1700000000,\"updatedAt\":1700000100,\"recencyAt\":1700000100}],\"nextCursor\":null}",
            "thread/read" => ThreadResponseJson,
            _ => "{}",
        };
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public Task NotifyAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default)
    {
        Calls.Add($"notify:{method}");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsRunning = false;
        return ValueTask.CompletedTask;
    }
}
