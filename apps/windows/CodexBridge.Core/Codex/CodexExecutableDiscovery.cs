namespace CodexBridge.Core.Codex;

public sealed record CodexExecutable(
    string Path,
    CodexLauncherKind LauncherKind)
{
    public string DisplayPath => Path;

    public bool IsWindowsShellShim => LauncherKind is not CodexLauncherKind.NativeExecutable;
}

public enum CodexLauncherKind
{
    NativeExecutable,
    CommandScript,
    PowerShellScript,
}

public sealed class CodexExecutableDiscovery
{
    public const string OverrideEnvironmentVariable = "CODEX_BRIDGE_CODEX_PATH";

    private readonly IReadOnlyList<string> configuredPaths;

    public CodexExecutableDiscovery(IEnumerable<string>? configuredPaths = null)
    {
        this.configuredPaths = (configuredPaths ?? Array.Empty<string>()).ToArray();
    }

    public CodexExecutable? Find()
    {
        var overridePath = Environment.GetEnvironmentVariable(OverrideEnvironmentVariable);
        var fromOverride = TryCreate(overridePath);
        if (fromOverride is not null)
        {
            return fromOverride;
        }

        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return FindFromPathEntries(pathEntries, configuredPaths);
    }

    public static CodexExecutable? FindFromPathEntries(
        IEnumerable<string> pathEntries,
        IEnumerable<string>? additionalPaths = null)
    {
        var entries = pathEntries
            .Concat(additionalPaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('"'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Prefer the native executable across all PATH entries. This avoids
        // launching a node/cmd shim when a native Codex binary is available.
        foreach (var fileName in NativeExecutableNames())
        {
            foreach (var entry in entries)
            {
                var candidate = TryCreate(Path.Combine(entry, fileName));
                if (candidate is not null)
                {
                    return candidate;
                }
            }
        }

        foreach (var fileName in CommandScriptNames())
        {
            foreach (var entry in entries)
            {
                var candidate = TryCreate(Path.Combine(entry, fileName));
                if (candidate is not null)
                {
                    return candidate;
                }
            }
        }

        foreach (var fileName in PowerShellScriptNames())
        {
            foreach (var entry in entries)
            {
                var candidate = TryCreate(Path.Combine(entry, fileName));
                if (candidate is not null)
                {
                    return candidate;
                }
            }
        }

        foreach (var configuredPath in additionalPaths ?? Array.Empty<string>())
        {
            var candidate = TryCreate(configuredPath);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> NativeExecutableNames()
    {
        yield return "codex.exe";
    }

    private static IEnumerable<string> CommandScriptNames()
    {
        yield return "codex.cmd";
        yield return "codex.bat";
    }

    private static IEnumerable<string> PowerShellScriptNames()
    {
        yield return "codex.ps1";
    }

    private static CodexExecutable? TryCreate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!File.Exists(expanded))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(expanded);
        var extension = Path.GetExtension(fullPath);
        var launcherKind = extension.ToLowerInvariant() switch
        {
            ".exe" => CodexLauncherKind.NativeExecutable,
            ".cmd" or ".bat" => CodexLauncherKind.CommandScript,
            ".ps1" => CodexLauncherKind.PowerShellScript,
            _ => (CodexLauncherKind?)null,
        };
        return launcherKind is null ? null : new CodexExecutable(fullPath, launcherKind.Value);
    }
}
