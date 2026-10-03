using System.Reflection;

namespace CodexBridge.App.Infrastructure;

public static class ProductInfo
{
    public static string Version =>
        typeof(ProductInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ProductInfo).Assembly.GetName().Version?.ToString(3)
        ?? throw new InvalidOperationException("Codex Bridge version metadata is unavailable.");
}
