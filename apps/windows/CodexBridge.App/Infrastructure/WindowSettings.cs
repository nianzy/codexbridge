using System.Text.Json;
using System.IO;

namespace CodexBridge.App.Infrastructure;

public sealed record WindowSettings(
    double Width = 1280,
    double Height = 800,
    double? Left = null,
    double? Top = null,
    double LeftPanelWidth = 280,
    double RightPanelWidth = 360,
    string Theme = "System",
    bool DraftExpanded = true);

public static class WindowSettingsStore
{
    public static string Path => System.IO.Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "settings.json");

    public static WindowSettings Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                return JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(Path)) ?? new WindowSettings();
            }
        }
        catch (Exception) { }
        return new WindowSettings();
    }

    public static void Save(WindowSettings settings)
    {
        Directory.CreateDirectory(CodexBridgeWindowsPaths.SupportDirectory);
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, Path, true);
    }
}
