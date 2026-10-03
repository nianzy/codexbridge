using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CodexBridge.App.Infrastructure;

public static class WorkspaceStateFingerprint
{
    public static string Compute(WorkspaceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        Append(builder, NormalizePath(snapshot.Workspace.Path));

        foreach (var selection in snapshot.Selection
                     .OrderBy(item => item.Source, StringComparer.Ordinal)
                     .ThenBy(item => item.SessionId, StringComparer.Ordinal)
                     .ThenBy(item => item.TurnId, StringComparer.Ordinal)
                     .ThenBy(item => item.Role, StringComparer.Ordinal))
        {
            Append(builder, selection.Source);
            Append(builder, selection.SessionId);
            Append(builder, selection.TurnId);
            Append(builder, selection.Role);
            Append(builder, selection.TextHash);
        }

        foreach (var item in snapshot.ContextItems)
        {
            Append(builder, item.Key);
            Append(builder, item.Type);
            Append(builder, item.Title);
            Append(builder, item.Source);
            Append(builder, item.ReferencePath);
            Append(builder, item.Content);
        }

        var handoff = snapshot.Handoff;
        foreach (var value in new[]
        {
            handoff.Target, handoff.Template, handoff.Title, handoff.Task, handoff.CurrentState,
            handoff.Constraints, handoff.NextAction,
            handoff.IncludeContextPack.ToString(), handoff.IncludeWorkspace.ToString(),
            handoff.IncludeGitState.ToString(), handoff.IncludeProjectFileSummary.ToString(),
        }) Append(builder, value);

        Append(builder, snapshot.Git.Branch);
        Append(builder, snapshot.Git.HeadCommit);
        Append(builder, snapshot.Git.StatusFingerprint);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length).Append(':').Append(value).Append('|');
    }

    private static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
