using System.Text;

namespace CodexBridge.App.Infrastructure;

public sealed record HandoffInput(
    string Target,
    string Template,
    string Title,
    string Task,
    string CurrentState,
    string Constraints,
    string NextAction,
    string WorkspaceName,
    string WorkspacePath,
    string Branch,
    IReadOnlyList<GitChangedFile> ChangedFiles,
    IReadOnlyList<ContextPackItem> ContextItems,
    int ProjectFileCount,
    bool IncludeContext,
    bool IncludeWorkspace,
    bool IncludeGit,
    bool IncludeProjectFiles);

public static class HandoffService
{
    public static string NormalizeUserTask(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var markerIndex = -1;
        for (var index = 0; index < lines.Length; index++)
        {
            var marker = lines[index].Trim().TrimStart('#').Trim();
            if (marker.Equals("My request:", StringComparison.OrdinalIgnoreCase)) markerIndex = index;
        }

        var wrapperSeen = markerIndex < 0 && lines.Any(line =>
            line.Trim().TrimStart('#').Trim().Equals("Files pasted by the user:", StringComparison.OrdinalIgnoreCase));
        if (wrapperSeen) return string.Empty;

        var result = new List<string>();
        foreach (var line in lines.Skip(markerIndex + 1))
        {
            var trimmed = line.Trim();
            if (trimmed.Equals("# Files pasted by the user:", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("Files pasted by the user:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (trimmed.Contains(@".codex\attachments\", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("/attachments/", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.StartsWith("Pasted text contains", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(line);
        }
        var normalized = string.Join(Environment.NewLine, result).Trim();
        return normalized.Length > 8000 ? string.Empty : normalized;
    }

    public static string Render(HandoffInput input)
    {
        var b = new StringBuilder();
        b.AppendLine("# Handoff").AppendLine().AppendLine($"Target: {input.Target}").AppendLine($"Title: {input.Title}").AppendLine();
        if (input.Template == "Debug Issue") b.AppendLine("## Problem").AppendLine().AppendLine(input.Task).AppendLine();
        else if (input.Template == "Review Changes") b.AppendLine("## Review Goal").AppendLine().AppendLine(input.Task).AppendLine();
        else b.AppendLine("## Goal").AppendLine().AppendLine(input.Task).AppendLine();
        b.AppendLine("## Current State").AppendLine().AppendLine(input.CurrentState).AppendLine();
        if (input.Template == "Plan Next Step") b.AppendLine("## Open Questions").AppendLine().AppendLine(input.Constraints).AppendLine();
        else b.AppendLine("## Constraints").AppendLine().AppendLine(input.Constraints).AppendLine();
        if (input.IncludeContext)
        {
            b.AppendLine("## Important Context").AppendLine();
            foreach (var item in input.ContextItems.OrderBy(item => item.Order))
                b.AppendLine($"### {item.Title}").AppendLine().AppendLine(item.Content).AppendLine();
        }
        if (input.IncludeWorkspace)
            b.AppendLine("## Workspace").AppendLine().AppendLine($"Workspace: {input.WorkspaceName}").AppendLine($"Path: {input.WorkspacePath}").AppendLine();
        if (input.IncludeGit)
        {
            b.AppendLine("## Git State").AppendLine().AppendLine($"Branch: {input.Branch}").AppendLine($"Modified: {input.ChangedFiles.Count(file => file.Status == "M")}").AppendLine($"Added: {input.ChangedFiles.Count(file => file.Status == "A")}").AppendLine($"Deleted: {input.ChangedFiles.Count(file => file.Status == "D")}").AppendLine().AppendLine("Changed files:");
            foreach (var file in input.ChangedFiles) b.AppendLine($"- {file.Status} {file.Path}");
            b.AppendLine();
        }
        if (input.IncludeProjectFiles) b.AppendLine("## Project File Summary").AppendLine().AppendLine($"Files: {input.ProjectFileCount}").AppendLine();
        b.AppendLine(input.Template == "Review Changes" ? "## Requested Review" : input.Template == "Debug Issue" ? "## Next Diagnostic Step" : input.Template == "Plan Next Step" ? "## Requested Decision" : "## Next Action").AppendLine().AppendLine(input.NextAction).AppendLine();
        return b.ToString();
    }
}
