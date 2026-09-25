using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace CodexBridge.App.Infrastructure;

public static class ThemeManager
{
    public static string Resolve(string mode)
    {
        if (mode is "Light" or "Dark") return mode;
        var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        return value is int number && number == 0 ? "Dark" : "Light";
    }

    public static Uri ResourceUri(string mode) => new($"Themes/{(Resolve(mode) == "Dark" ? "DarkColors" : "Colors")}.xaml", UriKind.Relative);

    public static void Apply(string mode)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        if (dictionaries.Count == 0)
        {
            dictionaries.Add(new ResourceDictionary { Source = ResourceUri(mode) });
            dictionaries.Add(new ResourceDictionary { Source = new Uri("Themes/Styles.xaml", UriKind.Relative) });
            return;
        }

        var previous = dictionaries[0];
        var requested = Resolve(mode);
        try
        {
            dictionaries[0] = new ResourceDictionary { Source = ResourceUri(requested) };
            if (dictionaries.Count == 1)
            {
                dictionaries.Insert(1, new ResourceDictionary { Source = new Uri("Themes/Styles.xaml", UriKind.Relative) });
            }
        }
        catch (Exception exception)
        {
            try
            {
                dictionaries[0] = previous;
            }
            catch (Exception rollbackException)
            {
                WriteThemeLog($"theme rollback failed; exception type={rollbackException.GetType().FullName}; message={rollbackException.Message}");
            }

            WriteThemeLog($"theme apply failed; requested={requested}; dictionary={ResourceUri(requested)}; exception type={exception.GetType().FullName}; message={exception.Message}");
            throw;
        }
    }

    private static void WriteThemeLog(string message)
    {
        new CodexAppLog(Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "Logs", "ui.log")).Write(message);
    }
}
