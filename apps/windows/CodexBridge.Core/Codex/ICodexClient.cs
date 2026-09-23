using System.Text.Json;

namespace CodexBridge.Core.Codex;

public interface ICodexClient : IAsyncDisposable
{
    CodexConnectionStatus Status { get; }

    string? ExecutablePath { get; }

    bool IsFaulted { get; }

    Task<CodexProbe> ProbeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CodexThreadSummary>> ListThreadsAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<CodexThreadSnapshot> ReadThreadAsync(
        string threadId,
        CancellationToken cancellationToken = default);
}

public interface ICodexAppServerTransport : IAsyncDisposable
{
    bool IsRunning { get; }

    string? ExecutablePath { get; }

    string? LastError { get; }

    bool IsFaulted { get; }

    IReadOnlyList<string> StderrLines { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task<JsonElement> RequestAsync(
        string method,
        System.Text.Json.Nodes.JsonObject? parameters,
        CancellationToken cancellationToken = default);

    Task NotifyAsync(
        string method,
        System.Text.Json.Nodes.JsonObject? parameters,
        CancellationToken cancellationToken = default);
}
