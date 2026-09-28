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
        b.AppendLine("# 任务交接").AppendLine().AppendLine($"目标：{input.Target}").AppendLine($"标题：{input.Title}").AppendLine();
        if (input.Template == "Debug Issue")
        {
            b.AppendLine("## 问题").AppendLine().AppendLine(input.Task).AppendLine();
            b.AppendLine("## 实际表现").AppendLine().AppendLine(input.CurrentState).AppendLine();
            b.AppendLine("## 预期表现").AppendLine().AppendLine(input.NextAction).AppendLine();
            b.AppendLine("## 证据").AppendLine().AppendLine(input.CurrentState).AppendLine();
            b.AppendLine("## 疑似区域").AppendLine().AppendLine(input.Task).AppendLine();
            b.AppendLine("## 约束").AppendLine().AppendLine(input.Constraints).AppendLine();
            b.AppendLine("## 下一步诊断").AppendLine().AppendLine(input.NextAction).AppendLine();
        }
        else if (input.Template == "Review Changes")
        {
            b.AppendLine("## 审查目标").AppendLine().AppendLine(input.Task).AppendLine();
            b.AppendLine("## 修改文件").AppendLine();
            b.AppendLine("## 相关差异").AppendLine().AppendLine(input.CurrentState).AppendLine();
            b.AppendLine("## 已知测试").AppendLine();
            b.AppendLine("## 风险").AppendLine().AppendLine(input.Constraints).AppendLine();
            b.AppendLine("## 审查请求").AppendLine().AppendLine(input.NextAction).AppendLine();
        }
        else if (input.Template == "Plan Next Step")
        {
            b.AppendLine("## 目标").AppendLine().AppendLine(input.Task).AppendLine();
            b.AppendLine("## 当前状态").AppendLine().AppendLine(input.CurrentState).AppendLine();
            b.AppendLine("## 待确认问题").AppendLine().AppendLine(input.Constraints).AppendLine();
            b.AppendLine("## 约束").AppendLine().AppendLine(input.Constraints).AppendLine();
            b.AppendLine("## 可选下一步").AppendLine().AppendLine(input.NextAction).AppendLine();
            b.AppendLine("## 请求决策").AppendLine().AppendLine(input.NextAction).AppendLine();
        }
        else
        {
            b.AppendLine("## 目标").AppendLine().AppendLine(input.Task).AppendLine();
            b.AppendLine("## 当前状态").AppendLine().AppendLine(input.CurrentState).AppendLine();
        }
        if (input.Template != "Debug Issue" && input.Template != "Review Changes" && input.Template != "Plan Next Step")
            b.AppendLine("## 约束").AppendLine().AppendLine(input.Constraints).AppendLine();
        if (input.IncludeContext)
        {
            b.AppendLine("## 重要上下文").AppendLine();
            foreach (var item in input.ContextItems.OrderBy(item => item.Order))
                b.AppendLine($"### {item.Title}").AppendLine().AppendLine(item.Content).AppendLine();
        }
        if (input.IncludeWorkspace)
            b.AppendLine("## 工作区").AppendLine().AppendLine($"工作区：{input.WorkspaceName}").AppendLine($"路径：{input.WorkspacePath}").AppendLine();
        if (input.IncludeGit)
        {
            b.AppendLine("## Git 状态").AppendLine().AppendLine($"分支：{input.Branch}").AppendLine($"修改：{input.ChangedFiles.Count(file => file.Status == "M")}").AppendLine($"新增：{input.ChangedFiles.Count(file => file.Status == "A")}").AppendLine($"删除：{input.ChangedFiles.Count(file => file.Status == "D")}").AppendLine().AppendLine("变更文件：");
            foreach (var file in input.ChangedFiles) b.AppendLine($"- {file.Status} {file.Path}");
            b.AppendLine();
        }
        if (input.IncludeProjectFiles) b.AppendLine("## 项目文件摘要").AppendLine().AppendLine($"文件数：{input.ProjectFileCount}").AppendLine();
        if (input.Template == "Continue Task") b.AppendLine("## 下一步操作").AppendLine().AppendLine(input.NextAction).AppendLine();
        return b.ToString();
    }
}
