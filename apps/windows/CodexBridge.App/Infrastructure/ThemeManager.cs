using Microsoft.Win32;
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
        if (dictionaries.Count == 0) return;
        dictionaries[0] = new ResourceDictionary { Source = ResourceUri(mode) };
        if (dictionaries.Count > 1)
        {
            dictionaries[1] = new ResourceDictionary { Source = new Uri("Themes/Styles.xaml", UriKind.Relative) };
        }
    }
}
