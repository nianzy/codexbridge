using System.Text.Json;
using System.IO;

namespace CodexBridge.App.Infrastructure;

public sealed record WorkspaceItem(string Name, string Path, DateTimeOffset LastOpened);

public static class WorkspaceStore
{
    public static string FilePath
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable("CODEX_BRIDGE_WORKSPACES_PATH");
            return string.IsNullOrWhiteSpace(overridePath)
                ? System.IO.Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "workspaces.json")
                : System.IO.Path.GetFullPath(overridePath);
        }
    }

    public static IReadOnlyList<WorkspaceItem> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<WorkspaceItem>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch { return []; }
    }

    public static void Save(IEnumerable<WorkspaceItem> workspaces)
    {
        Directory.CreateDirectory(CodexBridgeWindowsPaths.SupportDirectory);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(workspaces, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }
}
