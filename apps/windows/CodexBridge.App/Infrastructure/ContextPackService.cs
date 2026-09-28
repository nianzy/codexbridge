using System.Security.Cryptography;
using System.Text;

namespace CodexBridge.App.Infrastructure;

public sealed record ContextPackItem(string Type, string Title, string Source, string Content, string? ReferencePath, int Order, string Key);
public sealed record ContextPack(string Title, string WorkspaceName, string WorkspacePath, DateTimeOffset CreatedAt, IReadOnlyList<ContextPackItem> Items, int EstimatedCharacters);

public sealed class ContextPackService
{
    public static string ClassifySize(int characters) => characters < 20000 ? "Small" : characters <= 80000 ? "Medium" : "Large";
    public static string CreateKey(string type, string source, string reference, string content) => $"{type}|{source}|{reference}|{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))}";

    public ContextPack Create(string title, WorkspaceItem workspace, IEnumerable<ContextPackItem> items) =>
        new(title, workspace.Name, workspace.Path, DateTimeOffset.Now, items.OrderBy(item => item.Order).ToArray(), items.Sum(item => item.Content.Length));

    public string RenderMarkdown(ContextPack pack, string branch, int modifiedFiles)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# 上下文包").AppendLine().AppendLine($"工作区：{pack.WorkspaceName}").AppendLine($"路径：{pack.WorkspacePath}").AppendLine($"创建时间：{pack.CreatedAt:O}").AppendLine().AppendLine("## 项目状态").AppendLine().AppendLine($"分支：{branch}").AppendLine($"已修改文件：{modifiedFiles}").AppendLine();
        foreach (var group in pack.Items.GroupBy(item => item.Type))
        {
            builder.AppendLine(group.Key switch { "ChatGPTTurn" => "## ChatGPT 上下文", "CodexTurn" => "## Codex 上下文", "ProjectFile" => "## 项目文件", "GitDiff" => "## Git 差异", "Note" => "## 笔记", _ => "## 上下文" }).AppendLine();
            foreach (var item in group) builder.AppendLine($"### {item.Title}").AppendLine().AppendLine(item.Content).AppendLine();
        }
        return builder.ToString();
    }
}
