using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace CodexBridge.Core.Codex;

public sealed class CodexAppServerTransport : ICodexAppServerTransport
{
    private readonly CodexExecutableDiscovery discovery;
    private readonly TimeSpan requestTimeout;
    private readonly TimeSpan shutdownTimeout;
    private readonly Action<string>? diagnostics;
    private readonly ConcurrentDictionary<long, PendingRequest> pending = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly object stateGate = new();
    private readonly Queue<string> stderrLines = new();
    private readonly CancellationTokenSource lifetime = new();
    private long nextRequestId;
    private Process? process;
    private StreamWriter? input;
    private Task? stdoutTask;
    private Task? stderrTask;
    private Task? startTask;
    private bool disposed;
    private bool faulted;
    private Exception? faultException;
    private string? lastError;
    private long stdoutSequence;
    public string? LastProtocolDiagnostic { get; private set; }
    public long? LastProtocolFailurePosition { get; private set; }

    public CodexAppServerTransport(
        CodexExecutableDiscovery? discovery = null,
        TimeSpan? requestTimeout = null,
        TimeSpan? shutdownTimeout = null,
        Action<string>? diagnostics = null)
    {
        this.discovery = discovery ?? new CodexExecutableDiscovery();
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(15);
        this.shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(3);
        this.diagnostics = diagnostics;
    }

    public bool IsRunning
    {
        get
        {
            lock (stateGate)
            {
                return process?.HasExited == false;
            }
        }
    }

    public string? ExecutablePath { get; private set; }

    public bool IsFaulted
    {
        get
        {
            lock (stateGate)
            {
                return faulted;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (stateGate)
            {
                return lastError;
            }
        }
    }

    public IReadOnlyList<string> StderrLines
    {
        get
        {
            lock (stateGate)
            {
                return stderrLines.ToArray();
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfFaulted();
        Task task;
        lock (stateGate)
        {
            if (process?.HasExited == false)
            {
                return;
            }

            startTask ??= StartCoreAsync();
            task = startTask;
        }

        try
        {
            await task.WaitAsync(cancellationToken);
        }
        finally
        {
            lock (stateGate)
            {
                if (ReferenceEquals(startTask, task))
                {
                    startTask = null;
                }
            }
        }
    }

    public async Task<JsonElement> RequestAsync(
        string method,
        JsonObject? parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        await StartAsync(cancellationToken);
        ThrowIfDisposed();
        ThrowIfFaulted();

        var id = Interlocked.Increment(ref nextRequestId);
        var pendingRequest = new PendingRequest(method);
        if (!pending.TryAdd(id, pendingRequest))
        {
            throw new InvalidOperationException($"Duplicate Codex request ID {id}.");
        }

        try
        {
            var serialized = CodexJsonRpc.SerializeRequest(id, method, parameters);
            ReportOutboundFingerprint("request", id, method, serialized, parameters);
            if (string.Equals(method, "thread/list", StringComparison.Ordinal))
            {
                ReportRequestFingerprint(id, serialized);
            }

            await WriteLineAsync(serialized, cancellationToken);
            var completed = await Task.WhenAny(
                pendingRequest.Completion.Task,
                Task.Delay(requestTimeout, cancellationToken));
            if (completed == pendingRequest.Completion.Task)
            {
                return await pendingRequest.Completion.Task;
            }

            pending.TryRemove(id, out _);
            cancellationToken.ThrowIfCancellationRequested();
            throw new CodexRequestTimeoutException(method, requestTimeout);
        }
        catch
        {
            pending.TryRemove(id, out _);
            throw;
        }
    }

    public async Task NotifyAsync(
        string method,
        JsonObject? parameters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        await StartAsync(cancellationToken);
        ThrowIfFaulted();
        var serialized = CodexJsonRpc.SerializeNotification(method, parameters);
        ReportOutboundFingerprint("notification", null, method, serialized, parameters);
        await WriteLineAsync(serialized, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        Process? currentProcess;
        Task? currentStdout;
        Task? currentStderr;
        lock (stateGate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            currentProcess = process;
            currentStdout = stdoutTask;
            currentStderr = stderrTask;
            process = null;
            input = null;
        }

        lifetime.Cancel();
        FailPending(new CodexDisconnectedException("Codex app-server transport was disposed."));
        try
        {
            currentProcess?.StandardInput.Close();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }

        if (currentProcess is not null && !currentProcess.HasExited)
        {
            if (!currentProcess.WaitForExit((int)shutdownTimeout.TotalMilliseconds))
            {
                try
                {
                    currentProcess.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }

                currentProcess.WaitForExit();
            }
        }

        if (currentStdout is not null || currentStderr is not null)
        {
            var tasks = new[] { currentStdout, currentStderr }
                .Where(task => task is not null)
                .Cast<Task>()
                .ToArray();
            try
            {
                await Task.WhenAll(tasks).WaitAsync(shutdownTimeout);
            }
            catch (Exception) when (tasks.Length > 0)
            {
                // Process termination already completed the owned resources.
            }
        }

        currentProcess?.Dispose();
        writeGate.Dispose();
        lifetime.Dispose();
    }

    private async Task StartCoreAsync()
    {
        var executable = discovery.Find()
            ?? throw new CodexExecutableNotFoundException();
        var startInfo = CreateStartInfo(executable);
        ReportProcessFingerprint(executable, startInfo);
        var newProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        newProcess.Exited += OnProcessExited;

        try
        {
            if (!newProcess.Start())
            {
                throw new CodexDisconnectedException("Codex app-server process did not start.");
            }

            lock (stateGate)
            {
                process = newProcess;
                input = new StreamWriter(
                    newProcess.StandardInput.BaseStream,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true))
                {
                    AutoFlush = true,
                };
                ExecutablePath = executable.Path;
                lastError = null;
            }

            stdoutTask = ReadStdoutAsync(newProcess.StandardOutput, lifetime.Token);
            stderrTask = ReadStderrAsync(newProcess.StandardError, lifetime.Token);
            await Task.Yield();
        }
        catch
        {
            newProcess.Exited -= OnProcessExited;
            newProcess.Dispose();
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(CodexExecutable executable)
    {
        var strictUtf8 = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = strictUtf8,
            StandardOutputEncoding = strictUtf8,
            StandardErrorEncoding = strictUtf8,
        };

        if (executable.LauncherKind is CodexLauncherKind.NativeExecutable)
        {
            startInfo.FileName = executable.Path;
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
            return startInfo;
        }

        if (executable.LauncherKind is CodexLauncherKind.CommandScript)
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            // cmd.exe's /c rule requires the executable path and the complete
            // command to be quoted as one command-line argument. ArgumentList
            // escapes those quotes differently, so use the explicit Arguments
            // form for this shell-shim branch only.
            startInfo.Arguments = $"/d /s /c \"\"{executable.Path}\" app-server --stdio\"";
            return startInfo;
        }

        startInfo.FileName = "powershell.exe";
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(executable.Path);
        startInfo.ArgumentList.Add("app-server");
        startInfo.ArgumentList.Add("--stdio");
        return startInfo;
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        StreamWriter writer;
        lock (stateGate)
        {
            writer = input ?? throw new CodexDisconnectedException();
        }

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);
        }
        catch (ObjectDisposedException exception)
        {
            throw new CodexDisconnectedException(inner: exception);
        }
        catch (IOException exception)
        {
            throw new CodexDisconnectedException(inner: exception);
        }
        finally
        {
            writeGate.Release();
        }
    }

    private async Task ReadStdoutAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryParseStdoutLine(line, out var document))
                {
                    return;
                }

                using (document)
                {
                    HandleMessage(document.RootElement);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Fault(new CodexDisconnectedException(inner: exception));
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Fault(new CodexDisconnectedException());
            }
        }
    }

    private async Task ReadStderrAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                lock (stateGate)
                {
                    if (stderrLines.Count == 100)
                    {
                        stderrLines.Dequeue();
                    }

                    stderrLines.Enqueue(line);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException exception)
        {
            SetError(exception.Message);
        }
    }

    private void HandleMessage(JsonElement message)
    {
        if (!CodexJsonRpc.TryGetResponse(message, out var id, out var result, out var error))
        {
            // Notifications and server requests are intentionally ignored in
            // this read-only phase. No turn can be started by this client.
            return;
        }

        if (!pending.TryRemove(id, out var request))
        {
            return;
        }

        if (error is JsonElement errorValue)
        {
            var code = errorValue.TryGetProperty("code", out var codeValue)
                && codeValue.TryGetInt32(out var numericCode)
                ? numericCode
                : (int?)null;
            var messageText = errorValue.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString() ?? "Unknown app-server error"
                : "Unknown app-server error";
            request.Completion.TrySetException(new CodexServerException(request.Method, code, messageText));
            return;
        }

        if (result is JsonElement resultValue)
        {
            request.Completion.TrySetResult(resultValue.Clone());
        }
        else
        {
            request.Completion.TrySetException(
                new CodexProtocolException($"Codex response for '{request.Method}' did not contain a result."));
        }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        Fault(new CodexDisconnectedException("Codex app-server process exited."));
    }

    private bool TryParseStdoutLine(string line, out JsonDocument document)
    {
        var sequence = Interlocked.Increment(ref stdoutSequence);
        var bytes = Encoding.UTF8.GetBytes(line);
        var hasControl = line.Any(char.IsControl);
        var hasEscape = line.Contains('\u001b');
        var concatObjects = line.Contains("}{", StringComparison.Ordinal);
        try
        {
            document = JsonDocument.Parse(line);
            LastProtocolFailurePosition = null;
            LastProtocolDiagnostic = $"len={line.Length} utf8Bytes={bytes.Length} sha256={Sha256(bytes)} parse=true";
            var threadIds = IsThreadListResponse(document.RootElement)
                ? ExtractThreadIds(document.RootElement)
                : Array.Empty<string>();
            ReportDiagnostic($"stdout#{sequence} {LastProtocolDiagnostic} start={StartsWithObject(line)} end={EndsWithObject(line)} control={hasControl} esc={hasEscape} concatObjects={concatObjects} message={DescribeMessage(document.RootElement)} threadIdCount={threadIds.Count} sortedThreadIdsSha256={HashThreadIds(threadIds)}");
            return true;
        }
        catch (JsonException exception)
        {
            document = null!;
            var position = exception.BytePositionInLine;
            var character = GetCharacterCode(bytes, position);
            var readerInfo = ReadBoundaryInfo(bytes);
            var mask = BuildStructureMask(line, position);
            var structure = ScanStructure(line);
            LastProtocolFailurePosition = position;
            LastProtocolDiagnostic = $"len={line.Length} utf8Bytes={bytes.Length} sha256={Sha256(bytes)} parse=false position={position?.ToString() ?? "unknown"}";
            ReportDiagnostic($"stdout#{sequence} {LastProtocolDiagnostic} start={StartsWithObject(line)} end={EndsWithObject(line)} control={hasControl} esc={hasEscape} concatObjects={concatObjects} char={character} mask={mask} jsonPath={readerInfo.Path} lastToken={readerInfo.LastToken} depth={readerInfo.Depth} bytesConsumed={readerInfo.BytesConsumed} threadIdCount={readerInfo.ThreadIds.Count} sortedThreadIdsSha256={HashThreadIds(readerInfo.ThreadIds)} markers=method:{CountOccurrences(line, "\"method\"")},id:{CountOccurrences(line, "\"id\"")},result:{CountOccurrences(line, "\"result\"")},jsonrpc:{CountOccurrences(line, "\"jsonrpc\"")} topDepth={structure.FinalDepth} nestedEnvelope={structure.NestedEnvelope} recordSeparator={structure.RecordSeparator}");
            Fault(new CodexProtocolException("Codex app-server emitted invalid JSON.", exception));
            return false;
        }
    }

    private static string StartsWithObject(string line) => line.StartsWith('{') ? "{" : "other";

    private static string EndsWithObject(string line) => line.EndsWith('}') ? "}" : "other";

    private static string GetCharacterCode(byte[] bytes, long? bytePosition)
    {
        if (bytePosition is null)
        {
            return "unknown";
        }

        var index = (int)Math.Min(bytePosition.Value, Math.Max(0, bytes.Length - 1));
        var value = bytes.Length == 0 ? 0 : bytes[index];
        return $"U+{value:X4}";
    }

    private static string DescribeMessage(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return "unknown";
        }

        if (message.TryGetProperty("result", out _) || message.TryGetProperty("error", out _))
        {
            return $"response id={GetId(message)}";
        }

        if (message.TryGetProperty("method", out var method)
            && method.ValueKind == JsonValueKind.String)
        {
            return message.TryGetProperty("id", out _)
                ? $"request id={GetId(message)} method={method.GetString()}"
                : $"notification method={method.GetString()}";
        }

        return "unknown";
    }

    private static string GetId(JsonElement message) =>
        message.TryGetProperty("id", out var id) ? id.ToString() : "none";

    private bool IsThreadListResponse(JsonElement message)
    {
        if (!message.TryGetProperty("id", out var idElement)
            || !CodexJsonRpc.TryReadId(idElement, out var id)
            || !pending.TryGetValue(id, out var request))
        {
            return false;
        }

        return string.Equals(request.Method, "thread/list", StringComparison.Ordinal);
    }

    private void ReportRequestFingerprint(long id, string serialized)
    {
        try
        {
            using var document = JsonDocument.Parse(serialized);
            var parameters = document.RootElement.GetProperty("params");
            var sourceKinds = parameters.TryGetProperty("sourceKinds", out var source)
                && source.ValueKind == JsonValueKind.Array
                ? string.Join(',', source.EnumerateArray().Select(value => value.GetString() ?? "?"))
                : "(absent)";
            var bytes = Encoding.UTF8.GetBytes(serialized);
            ReportDiagnostic($"thread/list request id={id} utf8Bytes={bytes.Length} sha256={Sha256(bytes)} limit={GetScalar(parameters, "limit")} sortKey={GetScalar(parameters, "sortKey")} sortDirection={GetScalar(parameters, "sortDirection")} archived={GetScalar(parameters, "archived")} useStateDbOnly={GetScalar(parameters, "useStateDbOnly")} sourceKinds={sourceKinds} cwd={HasProperty(parameters, "cwd")} modelProviders={HasProperty(parameters, "modelProviders")} searchTerm={HasProperty(parameters, "searchTerm")} cursor={HasProperty(parameters, "cursor")}");
        }
        catch (JsonException)
        {
            ReportDiagnostic($"thread/list request id={id} fingerprint=unavailable");
        }
    }

    private void ReportOutboundFingerprint(
        string kind,
        long? id,
        string method,
        string serialized,
        JsonObject? parameters)
    {
        var bytes = Encoding.UTF8.GetBytes(serialized);
        var summary = string.Empty;
        if (string.Equals(method, "initialize", StringComparison.Ordinal) && parameters is not null)
        {
            var clientInfo = parameters["clientInfo"] as JsonObject;
            var capabilities = parameters["capabilities"] as JsonObject;
            var capabilityValues = capabilities is null
                ? "(none)"
                : string.Join(',', capabilities.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key}={pair.Value?.ToJsonString() ?? "null"}"));
            summary = $" clientInfo.name={clientInfo?["name"]?.GetValue<string>() ?? "(absent)"} clientInfo.version={clientInfo?["version"]?.GetValue<string>() ?? "(absent)"} capabilities={capabilityValues}";
        }

        ReportDiagnostic($"outbound kind={kind} id={id?.ToString() ?? "none"} method={method} utf8Bytes={bytes.Length} sha256={Sha256(bytes)}{summary}");
    }

    private void ReportProcessFingerprint(CodexExecutable executable, ProcessStartInfo startInfo)
    {
        var effectiveWorkingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            ? Environment.CurrentDirectory
            : startInfo.WorkingDirectory;
        var arguments = startInfo.ArgumentList.Count > 0
            ? string.Join(' ', startInfo.ArgumentList)
            : startInfo.Arguments;
        var environment = startInfo.Environment;
        var environmentLines = environment
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToArray();
        var environmentHash = Sha256(Encoding.UTF8.GetBytes(string.Join('\n', environmentLines)));
        var path = GetEnvironmentValue(environment, "PATH");
        ReportDiagnostic($"childProcess executable={executable.Path} fileName={startInfo.FileName} arguments={arguments} workingDirectory={effectiveWorkingDirectory} useShellExecute={startInfo.UseShellExecute} redirectStdin={startInfo.RedirectStandardInput} redirectStdout={startInfo.RedirectStandardOutput} redirectStderr={startInfo.RedirectStandardError} createNoWindow={startInfo.CreateNoWindow} stdinEncoding={DescribeEncoding(startInfo.StandardInputEncoding)} stdoutEncoding={DescribeEncoding(startInfo.StandardOutputEncoding)} stderrEncoding={DescribeEncoding(startInfo.StandardErrorEncoding)}");
        ReportDiagnostic($"childEnvironment USERPROFILE={GetEnvironmentValue(environment, "USERPROFILE") ?? "(unset)"} HOME={GetEnvironmentValue(environment, "HOME") ?? "(unset)"} HOMEDRIVE={GetEnvironmentValue(environment, "HOMEDRIVE") ?? "(unset)"} HOMEPATH={GetEnvironmentValue(environment, "HOMEPATH") ?? "(unset)"} COMSPEC={GetEnvironmentValue(environment, "COMSPEC") ?? "(unset)"} SYSTEMROOT={GetEnvironmentValue(environment, "SYSTEMROOT") ?? "(unset)"} TEMP={GetEnvironmentValue(environment, "TEMP") ?? "(unset)"} TMP={GetEnvironmentValue(environment, "TMP") ?? "(unset)"} TERM={SetState(environment, "TERM")} TERM_PROGRAM={SetState(environment, "TERM_PROGRAM")} CODEX_HOME={SetState(environment, "CODEX_HOME")} RUST_LOG={SetState(environment, "RUST_LOG")} LOG_FORMAT={SetState(environment, "LOG_FORMAT")} PATH.length={path?.Length ?? 0} PATH.sha256={Sha256(Encoding.UTF8.GetBytes(path ?? string.Empty))} PATHEXT={GetEnvironmentValue(environment, "PATHEXT") ?? "(unset)"} childEnvironment.sha256={environmentHash}");
    }

    private static string? GetEnvironmentValue(IDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) ? value : null;

    private static string SetState(IDictionary<string, string?> environment, string key) =>
        environment.TryGetValue(key, out var value) && value is not null ? "set" : "unset";

    private static string DescribeEncoding(Encoding? encoding) =>
        encoding is UTF8Encoding
        && encoding.GetPreamble().Length == 0
        && encoding.EncoderFallback is EncoderExceptionFallback
        && encoding.DecoderFallback is DecoderExceptionFallback
            ? "utf-8 strict"
            : encoding?.WebName ?? "(default)";

    private static string GetScalar(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) ? property.ToString() : "(absent)";

    private static bool HasProperty(JsonElement value, string name) => value.TryGetProperty(name, out _);

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static BoundaryInfo ReadBoundaryInfo(byte[] bytes)
    {
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        var lastToken = JsonTokenType.None;
        var frames = new List<PathFrame>();
        var lastPath = "$";
        var threadIds = new List<string>();
        try
        {
            while (reader.Read())
            {
                lastToken = reader.TokenType;
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                    {
                        var path = ConsumeValuePath(frames);
                        frames.Add(new PathFrame(reader.TokenType == JsonTokenType.StartArray, path));
                        lastPath = path;
                        break;
                    }
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        if (frames.Count > 0)
                        {
                            lastPath = frames[^1].Path;
                            frames.RemoveAt(frames.Count - 1);
                        }
                        break;
                    case JsonTokenType.PropertyName:
                        if (frames.Count > 0)
                        {
                            frames[^1].PendingProperty = SafePropertyName(reader.GetString());
                            lastPath = AppendProperty(frames[^1].Path, frames[^1].PendingProperty!);
                        }
                        break;
                    default:
                        lastPath = ConsumeValuePath(frames);
                        if (reader.TokenType == JsonTokenType.String
                            && IsThreadIdPath(lastPath))
                        {
                            var id = reader.GetString();
                            if (!string.IsNullOrWhiteSpace(id))
                            {
                                threadIds.Add(id);
                            }
                        }
                        break;
                }
            }
        }
        catch (JsonException)
        {
            return new BoundaryInfo(lastToken, reader.CurrentDepth, reader.BytesConsumed, lastPath, threadIds);
        }

        return new BoundaryInfo(lastToken, reader.CurrentDepth, reader.BytesConsumed, lastPath, threadIds);
    }

    private static string ConsumeValuePath(List<PathFrame> frames)
    {
        if (frames.Count == 0)
        {
            return "$";
        }

        var parent = frames[^1];
        if (parent.IsArray)
        {
            var path = $"{parent.Path}[{parent.NextArrayIndex}]";
            parent.NextArrayIndex++;
            return path;
        }

        var property = parent.PendingProperty ?? "?";
        parent.PendingProperty = null;
        return AppendProperty(parent.Path, property);
    }

    private static string AppendProperty(string path, string property) => $"{path}.{property}";

    private static string SafePropertyName(string? property)
    {
        if (string.IsNullOrEmpty(property))
        {
            return "?";
        }

        return property.All(character => char.IsLetterOrDigit(character) || character == '_' || character == '-')
            ? property
            : "?";
    }

    private static bool IsThreadIdPath(string path) =>
        path.StartsWith("$.result.data[", StringComparison.Ordinal)
        && path.EndsWith("].id", StringComparison.Ordinal);

    private static IReadOnlyList<string> ExtractThreadIds(JsonElement message)
    {
        if (!message.TryGetProperty("result", out var result)
            || !result.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return data.EnumerateArray()
            .Select(item => item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToArray();
    }

    private static string HashThreadIds(IEnumerable<string> threadIds)
    {
        var sorted = threadIds.OrderBy(id => id, StringComparer.Ordinal);
        return Sha256(Encoding.UTF8.GetBytes(string.Join('\n', sorted)));
    }

    private static string BuildStructureMask(string line, long? bytePosition)
    {
        var center = bytePosition is null ? line.Length : (int)Math.Min(line.Length, bytePosition.Value);
        var start = Math.Max(0, center - 16);
        var end = Math.Min(line.Length, center + 16);
        return string.Concat(line[start..end].Select(MaskCharacter));
    }

    private static char MaskCharacter(char value)
    {
        if (char.IsLetter(value)) return 'A';
        if (char.IsDigit(value)) return '0';
        if (char.IsWhiteSpace(value)) return '_';
        if (char.IsControl(value)) return '?';
        return value is '{' or '}' or '[' or ']' or ',' or ':' or '"' or '\\' ? value : '?';
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(needle, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += needle.Length;
        }

        return count;
    }

    private static (int FinalDepth, bool NestedEnvelope, bool RecordSeparator) ScanStructure(string value)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        var nestedEnvelope = false;
        var recordSeparator = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\u001e')
            {
                recordSeparator = true;
            }

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{' || character == '[')
            {
                if (depth > 0 && value.AsSpan(index).StartsWith("{\"jsonrpc\"", StringComparison.Ordinal))
                {
                    nestedEnvelope = true;
                }

                depth++;
            }
            else if (character == '}' || character == ']')
            {
                depth--;
            }
        }

        return (depth, nestedEnvelope, recordSeparator);
    }

    private void Fault(Exception exception)
    {
        var shouldFail = false;
        lock (stateGate)
        {
            if (!faulted && !disposed)
            {
                faulted = true;
                faultException = exception;
                lastError = exception.Message;
                shouldFail = true;
            }
        }

        if (shouldFail)
        {
            FailPending(exception);
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (var pair in pending.ToArray())
        {
            if (pending.TryRemove(pair.Key, out var request))
            {
                request.Completion.TrySetException(exception);
            }
        }
    }

    private void SetError(string value)
    {
        lock (stateGate)
        {
            lastError = value;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void ThrowIfFaulted()
    {
        Exception? exception;
        lock (stateGate)
        {
            exception = faultException;
        }

        if (exception is not null)
        {
            throw new CodexTransportFaultedException(exception);
        }
    }

    private void ReportDiagnostic(string message)
    {
        try
        {
            diagnostics?.Invoke(message);
        }
        catch
        {
        }
    }

    internal void MarkFaultForTesting(Exception exception) => Fault(exception);

    internal bool ProcessStdoutLineForTesting(string line) => TryParseStdoutLine(line, out var document)
        && DisposeParsedDocument(document);

    private static bool DisposeParsedDocument(JsonDocument document)
    {
        document.Dispose();
        return true;
    }

    private sealed class PathFrame
    {
        public PathFrame(bool isArray, string path)
        {
            IsArray = isArray;
            Path = path;
        }

        public bool IsArray { get; }

        public string Path { get; }

        public int NextArrayIndex { get; set; }

        public string? PendingProperty { get; set; }
    }

    private sealed record BoundaryInfo(
        JsonTokenType LastToken,
        int Depth,
        long BytesConsumed,
        string Path,
        IReadOnlyList<string> ThreadIds);

    private sealed class PendingRequest
    {
        public PendingRequest(string method)
        {
            Method = method;
        }

        public string Method { get; }

        public TaskCompletionSource<JsonElement> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
