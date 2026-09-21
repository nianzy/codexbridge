using System.IO;

namespace CodexBridge.App.Infrastructure;

public static class CodexBridgeWindowsPaths
{
    public static string SupportDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Codex Bridge");

    public static string DatabasePath => Path.Combine(SupportDirectory, "codex-bridge.sqlite");

    public static string CaptureInboxPath => Path.Combine(SupportDirectory, "CaptureInbox");
}
