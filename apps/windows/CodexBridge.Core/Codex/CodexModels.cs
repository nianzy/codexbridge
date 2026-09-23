namespace CodexBridge.Core.Codex;

public sealed record CodexInitializeResult(
    string? UserAgent,
    string? CodexHome,
    string? PlatformFamily,
    string? PlatformOs);

public sealed record CodexAccount(
    string? Type,
    string? PlanType,
    bool RequiresOpenaiAuth)
{
    public string DisplayLabel => Type switch
    {
        "apiKey" => "API Key",
        "chatgpt" when !string.IsNullOrWhiteSpace(PlanType) => $"ChatGPT ({PlanType})",
        "chatgpt" => "ChatGPT",
        "amazonBedrock" => "Amazon Bedrock",
        _ when RequiresOpenaiAuth => "未登录",
        _ => "未知账户",
    };
}

public sealed record CodexModelOption(
    string Id,
    string DisplayName,
    string Description,
    bool IsDefault,
    string DefaultReasoningEffort,
    IReadOnlyList<string> SupportedReasoningEfforts);

public sealed record CodexThreadSummary(
    string Id,
    string Title,
    string Preview,
    string? Cwd,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    DateTimeOffset? RecencyAt,
    bool Archived,
    string? ModelId);

public sealed record CodexMessage(string Id, string Text);

public sealed record CodexTurn(
    string Id,
    int Index,
    string Status,
    CodexMessage UserMessage,
    CodexMessage? AgentMessage);

public sealed record CodexThreadSnapshot(
    CodexThreadSummary Summary,
    IReadOnlyList<CodexTurn> Turns);

public sealed record CodexProbe(
    CodexInitializeResult Initialize,
    CodexAccount Account,
    IReadOnlyList<CodexModelOption> Models);

public enum CodexConnectionState
{
    Connected,
    NotFound,
    Disconnected,
    Error,
}

public sealed record CodexConnectionStatus(
    CodexConnectionState State,
    string Message)
{
    public static CodexConnectionStatus Connected(string message = "Connected") =>
        new(CodexConnectionState.Connected, message);

    public static CodexConnectionStatus NotFound(string message = "Not found") =>
        new(CodexConnectionState.NotFound, message);

    public static CodexConnectionStatus Disconnected(string message = "Disconnected") =>
        new(CodexConnectionState.Disconnected, message);

    public static CodexConnectionStatus Error(string message = "Error") =>
        new(CodexConnectionState.Error, message);
}

public sealed class CodexExecutableNotFoundException : Exception
{
    public CodexExecutableNotFoundException()
        : base("Codex executable was not found.")
    {
    }
}

public sealed class CodexDisconnectedException : Exception
{
    public CodexDisconnectedException(string message = "Codex app-server disconnected.", Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class CodexTransportFaultedException : Exception
{
    public CodexTransportFaultedException(Exception inner)
        : base("Codex app-server transport is faulted and must be recreated.", inner)
    {
    }
}

public sealed class CodexRequestTimeoutException : TimeoutException
{
    public CodexRequestTimeoutException(string method, TimeSpan timeout)
        : base($"Codex app-server request '{method}' timed out after {timeout.TotalSeconds:0.###} seconds.")
    {
        Method = method;
    }

    public string Method { get; }
}

public sealed class CodexProtocolException : Exception
{
    public CodexProtocolException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class CodexServerException : Exception
{
    public CodexServerException(string method, int? code, string message)
        : base($"Codex app-server '{method}' failed{(code is null ? string.Empty : $" ({code})")}: {message}")
    {
        Method = method;
        Code = code;
        ServerMessage = message;
    }

    public string Method { get; }

    public int? Code { get; }

    public string ServerMessage { get; }
}
