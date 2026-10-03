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
    public const string Same = "一致";
    public const string Changed = "已变化";
    public const string WillReplace = "将替换";
    public const string Missing = "缺失";
    public const string Unavailable = "不可比较";
    public const string WorkspaceMismatch = "工作区不一致";
    public const string Matchable = "可匹配";
    public const string NotCurrentlyMatchable = "当前不可匹配";
    public const string NotSet = "未设置";
    public const string Yes = "是";
    public const string No = "否";
    public const string SnapshotWorkspaceMismatchRestoreBlocked = "此工作现场不能在当前工作区恢复。";
    public const string CurrentGitDiffers = "当前 Git 状态与保存现场时不同";
    public const string GitUnavailableReason = "当前工作区没有可比较的 Git 状态。";
    public const string GitBranchChanged = "分支已变化";
    public const string GitHeadChanged = "HEAD 已变化";
    public const string GitWorkspaceStateChanged = "工作区文件状态已变化";
    public const string WorkspaceLabel = "工作区";
    public const string GitBranchLabel = "Git 分支";
    public const string GitHeadLabel = "Git HEAD";
    public const string GitWorkspaceStateLabel = "Git 工作区指纹";
    public const string SelectedTurnsLabel = "选中轮次";
    public const string ContextLabel = "上下文";
    public const string HandoffTitleLabel = "交接标题";
    public const string GitChangedFilesLabel = "变更文件";
    public const string GitFingerprintLabel = "指纹";
    public const string GitItemHeader = "项目";
    public const string GitSnapshotHeader = "工作现场";
    public const string GitCurrentHeader = "当前状态";
    public const string SnapshotPackageInvalid = "工作现场备份无效。";
    public const string SnapshotPackageMissingManifest = "工作现场备份缺少 manifest.json。";
    public const string SnapshotPackageMissingSnapshot = "工作现场备份缺少 snapshot.json。";
    public const string SnapshotPackageUnsupportedFiles = "工作现场备份包含不支持的文件。";
    public const string SnapshotPackageNewerVersion = "此备份来自更新版本的 Codex Bridge，当前版本无法导入。";
    public const string SnapshotPackageUnsupported = "不支持的工作现场备份格式。";
    public const string SnapshotPackageIdentityMismatch = "工作现场备份身份不一致。";
    public const string SnapshotPackageSchemaMismatch = "工作现场备份 Schema 不一致。";
    public const string SnapshotIntegrityFailed = "工作现场备份完整性校验失败。";
    public const string SnapshotAlreadyExists = "此工作现场已存在，不能重复导入。";
    public const string SnapshotImportMismatchWarning = "此工作现场导入后不能在当前工作区恢复，但仍可以导入并查看预览。";
    public const string SnapshotExported = "工作现场已导出。";
    public const string SnapshotImported = "工作现场已导入。";
    public const string QuickSaveSnapshot = "快速保存";
    public const string QuickSaveSucceeded = "工作现场已快速保存。";
    public static string QuickSaveFailed(string reason) => $"快速保存工作现场失败：{reason}";
    public const string SnapshotManual = "手动";
    public const string SnapshotAutomatic = "自动";
    public const string SnapshotTypeManual = "手动保存";
    public const string SnapshotTypeAutomatic = "自动保存";
    public const string AutoSaveBeforeWorkspaceSwitch = "切换工作区前自动保存工作现场";
    public const string WorkspaceSwitchConfirmTitle = "Codex Bridge";
    public const string WorkspaceSwitchUnsavedMessage = "当前上下文或交接中有未保存的内容。";
    public const string WorkspaceSwitchAutoSaveMessage = "切换前会先自动保存当前工作现场，然后清空当前界面内容并切换工作区。";
    public const string WorkspaceSwitchNoAutoSaveMessage = "切换工作区后，当前界面中的这些内容将被清空。";
    public const string WorkspaceSwitchConfirmQuestion = "确认切换工作区吗？";
    public const string SwitchWorkspace = "切换工作区";
    public static string WorkspaceSwitchConfirmationMessage(bool autoSaveEnabled) =>
        $"{WorkspaceSwitchUnsavedMessage}\n\n{(autoSaveEnabled ? WorkspaceSwitchAutoSaveMessage : WorkspaceSwitchNoAutoSaveMessage)}\n\n{WorkspaceSwitchConfirmQuestion}";
    public const string PromptSaveBeforeExit = "退出应用前提示保存工作现场";
    public const string AutoSnapshotRetention = "自动工作现场保留数量";
    public const string AutoSnapshotRetentionHint = "仅清理自动保存的工作现场，不会删除手动保存、快速保存或导入的工作现场。";
    public const string AutoSnapshotSaved = "工作现场已自动保存。";
    public const string AutoSnapshotUnchanged = "当前工作现场无变化，无需自动保存。";
    public const string AutoSnapshotFailed = "自动保存工作现场失败。";
    public const string AutoSnapshotSwitchFailed = "自动保存工作现场失败，工作区未切换。";
    public const string AutoSnapshotCleanupFailed = "工作现场已自动保存，但旧自动现场清理失败。";
    public const string UnsavedSnapshotExitTitle = "Codex Bridge";
    public const string UnsavedSnapshotExitMessage = "当前工作现场有未保存的变化。";
    public const string SaveAndExit = "保存并退出";
    public const string ExitWithoutSaving = "直接退出";
    public const string ExitSnapshotSaveFailed = "工作现场保存失败，应用未退出。";
    public const string SnapshotOperationErrorTitle = "Codex Bridge";
    public const string SnapshotRetentionValidation = "请输入 3 到 50 之间的整数。";
    public static string SnapshotExportFailed(string reason) => $"工作现场导出失败：{reason}";
    public static string SnapshotImportFailed(string reason) => $"工作现场导入失败：{reason}";

    public static string DisplayRole(string? value) => value switch
    {
        "User" => "用户",
        "Assistant" => "助手",
        "System" => "系统",
        _ => string.IsNullOrWhiteSpace(value) ? Unavailable : value,
    };

    public static string DisplayContextType(string? value) => value switch
    {
        "ProjectFile" => "项目文件",
        "ConversationTurn" => "会话轮次",
        "GitDiff" => "Git 差异",
        "Note" => "笔记",
        _ => string.IsNullOrWhiteSpace(value) ? Unavailable : value,
    };

    public static string DisplayContextTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Unavailable;
        return value.StartsWith("File · ", StringComparison.Ordinal)
            ? $"文件 · {value[7..]}"
            : value;
    }

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
