using Microsoft.Win32;

namespace CodexBridge.App.Infrastructure;

public sealed record NativeMessagingStatus(
    bool ChromeInstalled,
    bool EdgeInstalled)
{
    public bool IsInstalled => ChromeInstalled || EdgeInstalled;

    public string DisplayText => IsInstalled ? "Installed" : "Not installed";
}

public static class NativeMessagingStatusDetector
{
    private const string HostName = "app.codexbridge.nativehost";

    public static NativeMessagingStatus Detect()
    {
        return new NativeMessagingStatus(
            IsRegistered($"Software\\Google\\Chrome\\NativeMessagingHosts\\{HostName}"),
            IsRegistered($"Software\\Microsoft\\Edge\\NativeMessagingHosts\\{HostName}"));
    }

    private static bool IsRegistered(string subKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
        return key?.GetValue(null) is string manifestPath
            && !string.IsNullOrWhiteSpace(manifestPath);
    }
}
