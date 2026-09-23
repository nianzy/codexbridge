using System.IO;
using System.Text;

namespace CodexBridge.App.Infrastructure;

public interface ICodexAppLog
{
    void Write(string message);
}

public sealed class CodexAppLog : ICodexAppLog
{
    private readonly object gate = new();
    private readonly string path;

    public CodexAppLog(string? path = null)
    {
        this.path = path ?? Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "Logs", "codex-app.log");
    }

    public void Write(string message)
    {
        try
        {
            lock (gate)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(
                    path,
                    $"{DateTimeOffset.Now:O} {Sanitize(message)}{Environment.NewLine}",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Diagnostics must never prevent the app from starting or refreshing.
        }
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');
}

public sealed class NullCodexAppLog : ICodexAppLog
{
    public static NullCodexAppLog Instance { get; } = new();

    private NullCodexAppLog()
    {
    }

    public void Write(string message)
    {
    }
}
