using System.Text.Json;
using System.Text.Json.Nodes;
using CodexBridge.Core.Codex;

var candidates = DiscoverCandidates();
if (candidates.Count == 0)
{
    Console.WriteLine("Codex executable: not found");
    return 2;
}

var diagnosticMode = args.FirstOrDefault(argument => argument.StartsWith("--repeat-", StringComparison.OrdinalIgnoreCase)
    || argument.Equals("--limit-scan", StringComparison.OrdinalIgnoreCase)
    || argument.Equals("--page-scan", StringComparison.OrdinalIgnoreCase)
    || argument.Equals("--delay-scan", StringComparison.OrdinalIgnoreCase));
if (diagnosticMode is not null)
{
    var executable = candidates[0].Path;
    return diagnosticMode switch
    {
        "--repeat-thread-list" => await RepeatThreadListAsync(executable, 10),
        "--repeat-process" => await RepeatProcessAsync(executable, 10),
        "--limit-scan" => await LimitScanAsync(executable),
        "--page-scan" => await PageScanAsync(executable),
        "--delay-scan" => await DelayScanAsync(executable),
        _ => 2,
    };
}

foreach (var candidate in candidates)
{
    Console.WriteLine($"=== {candidate.Label} ===");
    Console.WriteLine($"executable: {candidate.Path}");
    try
    {
        if (args.Contains("--wpf-equivalent", StringComparer.OrdinalIgnoreCase))
        {
            await ProbeWpfEquivalentAsync(candidate.Path);
        }
        else
        {
            await ProbeExecutableAsync(candidate.Path);
        }
    }
    catch (Exception exception)
    {
        Console.WriteLine($"probe error: {exception.GetType().Name}: {exception.Message}");
    }
}

return 0;

static async Task<int> RepeatThreadListAsync(string executablePath, int count)
{
    await using var transport = await SetupWpfEquivalentTransportAsync(executablePath);
    for (var iteration = 1; iteration <= count; iteration++)
    {
        var result = await RunThreadListOnceAsync(transport, 100);
        PrintExperimentResult($"iteration={iteration}", result);
        if (!result.ParseSuccess || transport.IsFaulted)
        {
            Console.WriteLine("stopped: transport faulted after first protocol failure");
            break;
        }
    }

    return 0;
}

static async Task<int> RepeatProcessAsync(string executablePath, int count)
{
    for (var iteration = 1; iteration <= count; iteration++)
    {
        await using var transport = await SetupWpfEquivalentTransportAsync(executablePath);
        var result = await RunThreadListOnceAsync(transport, 100);
        PrintExperimentResult($"iteration={iteration}", result);
    }

    return 0;
}

static async Task<int> LimitScanAsync(string executablePath)
{
    foreach (var limit in new[] { 1, 2, 5, 10, 15, 20, 30, 50, 100 })
    {
        await using var transport = await SetupWpfEquivalentTransportAsync(executablePath);
        var result = await RunThreadListOnceAsync(transport, limit);
        PrintExperimentResult($"limit={limit}", result);
    }

    return 0;
}

static async Task<int> DelayScanAsync(string executablePath)
{
    foreach (var delay in new[] { 0, 100, 500, 1000 })
    {
        await using var transport = await SetupWpfEquivalentTransportAsync(executablePath);
        for (var iteration = 1; iteration <= 5; iteration++)
        {
            if (delay > 0)
            {
                await Task.Delay(delay);
            }

            var result = await RunThreadListOnceAsync(transport, 100);
            PrintExperimentResult($"delayMs={delay} iteration={iteration}", result);
            if (!result.ParseSuccess || transport.IsFaulted)
            {
                Console.WriteLine($"delayMs={delay} stopped: transport faulted");
                break;
            }
        }
    }

    return 0;
}

static async Task<int> PageScanAsync(string executablePath)
{
    await using var transport = await SetupWpfEquivalentTransportAsync(executablePath);
    string? cursor = null;
    for (var page = 1; page <= 20; page++)
    {
        var parameters = new JsonObject
        {
            ["limit"] = 5,
            ["sortKey"] = "updated_at",
            ["sortDirection"] = "desc",
            ["archived"] = false,
            ["useStateDbOnly"] = true,
        };
        if (cursor is not null)
        {
            parameters["cursor"] = cursor;
        }

        try
        {
            var response = await transport.RequestAsync("thread/list", parameters);
            var count = response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
                ? data.GetArrayLength()
                : 0;
            cursor = response.TryGetProperty("nextCursor", out var next)
                && next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
            var diagnostic = FindLatestResponseDiagnostic(transport);
            Console.WriteLine($"page={page} cursorPresent={(page > 1).ToString().ToLowerInvariant()} bytes={ExtractExperimentValue(diagnostic, "utf8Bytes")} sha256={ExtractExperimentValue(diagnostic, "sha256")} parse=true returnedCount={count}");
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }
        catch (Exception exception)
        {
            var diagnostic = FindLatestResponseDiagnostic(transport);
            Console.WriteLine($"page={page} cursorPresent={(page > 1).ToString().ToLowerInvariant()} bytes={ExtractExperimentValue(diagnostic, "utf8Bytes")} sha256={ExtractExperimentValue(diagnostic, "sha256")} parse=false returnedCount=0 failurePosition={FindLatestFailurePosition(transport)?.ToString() ?? "(none)"} error={exception.GetType().Name}");
            break;
        }
    }

    return 0;
}

static string ExtractExperimentValue(string? diagnostic, string key)
{
    if (diagnostic is null)
    {
        return "(none)";
    }

    var marker = key + "=";
    var start = diagnostic.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0)
    {
        return "(none)";
    }

    start += marker.Length;
    var end = diagnostic.IndexOf(' ', start);
    return end < 0 ? diagnostic[start..] : diagnostic[start..end];
}

static async Task<CodexAppServerTransport> SetupWpfEquivalentTransportAsync(string executablePath)
{
    var transport = new CodexAppServerTransport(new CodexExecutableDiscovery(new[] { executablePath }));
    try
    {
        await transport.StartAsync();
        await transport.RequestAsync("initialize", new JsonObject
        {
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "codexbridge-windows",
                ["title"] = "Codex Bridge",
                ["version"] = "3.0.0",
            },
            ["capabilities"] = new JsonObject { ["experimentalApi"] = false },
        });
        await transport.NotifyAsync("initialized", new JsonObject());
        await transport.RequestAsync("account/read", new JsonObject { ["refreshToken"] = false });
        await transport.RequestAsync("model/list", new JsonObject { ["includeHidden"] = false, ["limit"] = 100 });
        return transport;
    }
    catch
    {
        await transport.DisposeAsync();
        throw;
    }
}

static async Task<ExperimentResult> RunThreadListOnceAsync(ICodexAppServerTransport transport, int limit)
{
    var parameters = new JsonObject
    {
        ["limit"] = limit,
        ["sortKey"] = "updated_at",
        ["sortDirection"] = "desc",
        ["archived"] = false,
        ["useStateDbOnly"] = true,
    };
    try
    {
        var response = await transport.RequestAsync("thread/list", parameters);
        var count = response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.GetArrayLength()
            : 0;
        return new ExperimentResult(true, FindLatestResponseDiagnostic(transport), count, null, null);
    }
    catch (Exception exception)
    {
        return new ExperimentResult(false, FindLatestResponseDiagnostic(transport), 0, FindLatestFailurePosition(transport), exception.GetType().Name);
    }
}

static void PrintExperimentResult(string prefix, ExperimentResult result)
{
    Console.WriteLine($"{prefix} bytes={result.Bytes} sha256={result.Hash ?? "(none)"} parse={(result.ParseSuccess ? "true" : "false")} threadCount={result.ThreadCount} failurePosition={result.FailurePosition?.ToString() ?? "(none)"} error={result.Error ?? "(none)"}");
}

static string? FindLatestResponseDiagnostic(ICodexAppServerTransport transport) =>
    transport is CodexAppServerTransport concrete
        ? concrete.LastProtocolDiagnostic
        : null;

static long? FindLatestFailurePosition(ICodexAppServerTransport transport) =>
    transport is CodexAppServerTransport concrete
        ? concrete.LastProtocolFailurePosition
        : null;

static async Task ProbeExecutableAsync(string executablePath)
{
    await using var transport = new CodexAppServerTransport(
        new CodexExecutableDiscovery(new[] { executablePath }));
    await transport.StartAsync();

    var initialize = await transport.RequestAsync("initialize", new JsonObject
    {
        ["clientInfo"] = new JsonObject
        {
            ["name"] = "codexbridge-probe",
            ["title"] = "Codex Bridge Probe",
            ["version"] = "3.0.0-probe",
        },
        ["capabilities"] = new JsonObject { ["experimentalApi"] = false },
    });
    await transport.NotifyAsync("initialized", new JsonObject());

    var initializeResult = CodexAppServerClient.ParseInitialize(initialize);
    Console.WriteLine($"initialize.codexHome: {initializeResult.CodexHome ?? "(null)"}");
    Console.WriteLine($"initialize.platform: {initializeResult.PlatformFamily ?? "(null)"}/{initializeResult.PlatformOs ?? "(null)"}");
    PrintHomeComparison(initializeResult.CodexHome);

    var account = await transport.RequestAsync("account/read", new JsonObject { ["refreshToken"] = false });
    var parsedAccount = CodexAppServerClient.ParseAccount(account);
    Console.WriteLine($"account/read: account={(parsedAccount.Type ?? "null")}, requiresOpenaiAuth={parsedAccount.RequiresOpenaiAuth}");

    var sourceGroups = new (string Name, string[]? SourceKinds)[]
    {
        ("omitted", null),
        ("cli", new[] { "cli" }),
        ("vscode", new[] { "vscode" }),
        ("appServer", new[] { "appServer" }),
        ("cli,vscode,appServer", new[] { "cli", "vscode", "appServer" }),
        ("all-listed", new[] { "cli", "vscode", "exec", "appServer", "unknown" }),
    };

    var results = new List<ThreadListResult>();
    foreach (var group in sourceGroups)
    {
        var result = await ListThreadsAsync(transport, group.SourceKinds, useStateDbOnly: false);
        results.Add(result);
        Console.WriteLine($"thread/list sourceKinds={group.Name}, useStateDbOnly=false: {result.Threads.Count} threads");
    }

    foreach (var group in results)
    {
        var stateDbResult = await ListThreadsAsync(transport, group.SourceKinds, useStateDbOnly: true);
        Console.WriteLine($"thread/list sourceKinds={FormatSourceKinds(group.SourceKinds)}, useStateDbOnly=true: {stateDbResult.Threads.Count} threads");
    }

    var latest = results.Where(item => item.Threads.Count > 0).SelectMany(item => item.Threads)
        .GroupBy(thread => thread.Id, StringComparer.Ordinal).Select(group => group.First())
        .OrderByDescending(thread => thread.UpdatedAt).FirstOrDefault();
    if (latest is null)
    {
        Console.WriteLine("thread/read: skipped (no thread returned)");
        return;
    }

    var readResult = await transport.RequestAsync("thread/read", new JsonObject
    {
        ["threadId"] = latest.Id,
        ["includeTurns"] = true,
    });
    if (!readResult.TryGetProperty("thread", out var thread))
    {
        Console.WriteLine($"thread/read: id={latest.Id}, invalid response");
        return;
    }

    var parsedTurns = CodexAppServerClient.ParseTurns(thread);
    var agentCount = parsedTurns.Count(turn => turn.AgentMessage is not null);
    Console.WriteLine($"thread/read: id={latest.Id}, name/preview={latest.Title}, cwd={latest.Cwd ?? "(null)"}, turns={parsedTurns.Count}, userMessage={parsedTurns.Count}, agentMessage={agentCount}");
    Console.WriteLine("thread/read: reasoning/internal items ignored by parser");
}

static async Task ProbeWpfEquivalentAsync(string executablePath)
{
    Console.WriteLine("mode: --wpf-equivalent");
    PrintProcessEnvironment();
    await using var client = new CodexAppServerClient(
        discovery: new CodexExecutableDiscovery(new[] { executablePath }),
        diagnostics: message => Console.WriteLine($"diagnostic: {message}"));
    var probe = await client.ProbeAsync();
    Console.WriteLine($"initialize.codexHome: {probe.Initialize.CodexHome ?? "(null)"}");
    var threads = await client.ListThreadsAsync(100);
    Console.WriteLine($"thread/list: {threads.Count} threads");
    var first = threads.FirstOrDefault();
    if (first is null)
    {
        Console.WriteLine("thread/read: skipped (no thread returned)");
        return;
    }

    var snapshot = await client.ReadThreadAsync(first.Id);
    Console.WriteLine($"thread/read: id={first.Id}, turns={snapshot.Turns.Count}, userMessage={snapshot.Turns.Count}, agentMessage={snapshot.Turns.Count(turn => turn.AgentMessage is not null)}");
    Console.WriteLine("thread/read: reasoning/internal items ignored by parser");
}

static void PrintProcessEnvironment()
{
    Console.WriteLine($"workingDirectory: {Environment.CurrentDirectory}");
    Console.WriteLine($"USERPROFILE: {Environment.GetEnvironmentVariable("USERPROFILE") ?? "(unset)"}");
    Console.WriteLine($"CODEX_HOME set: {Environment.GetEnvironmentVariable("CODEX_HOME") is not null}");
    Console.WriteLine($"processArguments: {string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(argument => argument.Contains(' ') ? "(arg)" : argument))}");
}

static async Task<ThreadListResult> ListThreadsAsync(ICodexAppServerTransport transport, string[]? sourceKinds, bool useStateDbOnly)
{
    var parameters = new JsonObject
    {
        ["limit"] = 100,
        ["sortKey"] = "updated_at",
        ["sortDirection"] = "desc",
        ["archived"] = false,
        ["useStateDbOnly"] = useStateDbOnly,
    };
    if (sourceKinds is not null)
    {
        var array = new JsonArray();
        foreach (var sourceKind in sourceKinds)
        {
            array.Add(JsonValue.Create(sourceKind));
        }
        parameters["sourceKinds"] = array;
    }

    var response = await transport.RequestAsync("thread/list", parameters);
    var threads = new List<CodexThreadSummary>();
    if (response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in data.EnumerateArray())
        {
            var parsed = CodexAppServerClient.ParseThreadSummary(item);
            if (parsed is not null)
            {
                threads.Add(parsed);
            }
        }
    }
    return new ThreadListResult(sourceKinds, threads);
}

static void PrintHomeComparison(string? initializeHome)
{
    var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
    Console.WriteLine($"expected.codexHome: {expected}");
    Console.WriteLine($"CODEX_HOME: {(configured is null ? "(unset)" : configured)}");
    Console.WriteLine($"codexHome.matches.expected: {PathsEqual(initializeHome, expected)}");
    PrintHomeStats("initialize.codexHome", initializeHome);
    if (!PathsEqual(initializeHome, expected))
    {
        PrintHomeStats("expected.codexHome", expected);
    }
}

static void PrintHomeStats(string label, string? path)
{
    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
    {
        Console.WriteLine($"{label}: exists=false");
        return;
    }

    var sessions = Path.Combine(path, "sessions");
    var archived = Path.Combine(path, "archived_sessions");
    var history = Path.Combine(path, "history.jsonl");
    var stateSqliteFiles = Directory.EnumerateFiles(path, "state*.sqlite", SearchOption.TopDirectoryOnly).Count();
    var sqliteFiles = Directory.EnumerateFiles(path, "*.sqlite", SearchOption.TopDirectoryOnly).Count();
    var sessionsJsonl = Directory.Exists(sessions) ? Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories).Count() : 0;
    Console.WriteLine($"{label}: exists=true, sessions={Directory.Exists(sessions)}, archived_sessions={Directory.Exists(archived)}, history.jsonl={File.Exists(history)}, sqliteDir={Directory.Exists(Path.Combine(path, "sqlite"))}, stateSqliteFiles={stateSqliteFiles}, sqliteFiles={sqliteFiles}, sessionsJsonl={sessionsJsonl}");
}

static bool PathsEqual(string? left, string right) => !string.IsNullOrWhiteSpace(left) && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

static string FormatSourceKinds(string[]? sourceKinds) => sourceKinds is null ? "omitted" : string.Join(',', sourceKinds);

static IReadOnlyList<ProbeCandidate> DiscoverCandidates()
{
    var paths = new List<ProbeCandidate>();
    var overridePath = Environment.GetEnvironmentVariable(CodexExecutableDiscovery.OverrideEnvironmentVariable);
    if (!string.IsNullOrWhiteSpace(overridePath) && File.Exists(overridePath))
    {
        paths.Add(new ProbeCandidate("override", Path.GetFullPath(overridePath)));
        return paths;
    }

    var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "codex.cmd");
    if (File.Exists(npm))
    {
        paths.Add(new ProbeCandidate("npm-shim", npm));
    }

    var nativeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
    if (Directory.Exists(nativeRoot))
    {
        foreach (var native in Directory.EnumerateFiles(nativeRoot, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            paths.Add(new ProbeCandidate("native", native));
        }
    }

    var discovered = new CodexExecutableDiscovery().Find();
    if (discovered is not null)
    {
        paths.Add(new ProbeCandidate("PATH", discovered.Path));
    }

    return paths.GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToArray();
}

file sealed record ProbeCandidate(string Label, string Path);
file sealed record ThreadListResult(string[]? SourceKinds, IReadOnlyList<CodexThreadSummary> Threads);
file sealed record ExperimentResult(bool ParseSuccess, string? Diagnostic, int ThreadCount, long? FailurePosition, string? Error)
{
    public int? Bytes => ParseSuccess ? ExtractInt(Diagnostic, "utf8Bytes") : ExtractInt(Diagnostic, "utf8Bytes");
    public string? Hash => ExtractValue(Diagnostic, "sha256");

    private static int? ExtractInt(string? value, string key)
    {
        var raw = ExtractValue(value, key);
        return int.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static string? ExtractValue(string? value, string key)
    {
        if (value is null)
        {
            return null;
        }

        var marker = key + "=";
        var start = value.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = value.IndexOf(' ', start);
        return (end < 0 ? value[start..] : value[start..end]).Trim();
    }
}
