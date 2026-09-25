using System.Diagnostics;
using System.Text;
using System.IO;

namespace CodexBridge.App.Infrastructure;

public sealed record GitChangedFile(string Status, string Path);
public sealed record GitStatusSnapshot(string Branch, IReadOnlyList<GitChangedFile> Files, bool GitAvailable, string? Error = null)
{
    public int ModifiedCount => Files.Count(file => file.Status == "M");
    public int AddedCount => Files.Count(file => file.Status == "A");
    public int DeletedCount => Files.Count(file => file.Status == "D");
}

public sealed class GitService
{
    public async Task<GitStatusSnapshot> GetStatusAsync(string workspace, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(Path.Combine(workspace, ".git"))) return new("", [], false, "Git not found");
            var branch = (await RunAsync(workspace, ["branch", "--show-current"], cancellationToken)).Trim();
            var status = await RunAsync(workspace, ["status", "--short"], cancellationToken);
            return new(branch, ParseStatus(status), true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new("", [], false, "Git not found");
        }
    }

    public async Task<string> GetDiffAsync(string workspace, string relativePath, CancellationToken cancellationToken = default)
    {
        var diff = await RunAsync(workspace, ["diff", "--no-ext-diff", "--", relativePath], cancellationToken);
        return Encoding.UTF8.GetByteCount(diff) > 1024 * 1024 ? "Diff too large" : diff;
    }

    public static IReadOnlyList<GitChangedFile> ParseStatus(string output) => output
        .Replace("\r\n", "\n")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.Length >= 3)
        .Select(line => new GitChangedFile(ParseCode(line[..2]), line[3..].Trim()))
        .ToArray();

    private static string ParseCode(string code) => code.Contains('D') ? "D" : code.Contains('A') || code.Contains('?') ? "A" : "M";

    private static async Task<string> RunAsync(string workspace, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = workspace, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Git not found");
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Trim());
        return stdout;
    }
}
