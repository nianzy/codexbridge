namespace CodexBridge.App.Infrastructure;

public static class UiStrings
{
    public const string Ready = "就绪";
    public const string Loading = "正在加载...";
    public const string Copied = "已复制";
    public const string HandoffCopied = "交接内容已复制。";
    public const string HandoffGenerated = "交接内容已生成，请确认后再复制。";
    public const string Disconnected = "未连接";
    public const string Connected = "已连接";
    public const string GitNotFound = "未检测到 Git 仓库";
    public const string NoSession = "暂无会话";
    public const string NoDraft = "暂无草稿";
    public const string NoContext = "暂无上下文";
    public const string SnapshotRenamed = "工作现场已重命名。";
    public const string SnapshotDeleted = "工作现场已删除。";

    public static string CopyFailed(string reason) => $"复制失败：{reason}";
    public static string Exported(string path) => $"已导出：{path}";
    public static string SelectedTurns(int count, int total) => $"已选择 {count} / {total} 个轮次";
    public static string SelectedContext(int count) => $"已选择：{count} 个上下文项";
    public static string ContextCharacters(int count) => $"{count} 个字符";
    public static string SelectedContextSummary(int count) => $"已选择：{count} 个上下文项";
    public static string CharacterSummary(int count) => $"字符数：{count}";
    public static string SizeState(string value) => value switch
    {
        "Small" => "小",
        "Medium" => "中",
        "Large" => "大",
        _ => value,
    };
    public static string HandoffTemplateName(string value) => value switch
    {
        "Continue Task" => "继续任务",
        "Debug Issue" => "调试问题",
        "Review Changes" => "审查更改",
        "Plan Next Step" => "规划下一步",
        _ => value,
    };
    public static string Workspace(string name) => $"工作区：{name}";
    public static string Branch(string branch) => $"分支：{branch}";
    public static string ChangedFiles(int count) => $"已修改文件：{count} 个";
}
