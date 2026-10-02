namespace CodexBridge.App.Infrastructure;

public sealed record SnapshotPreviewCurrentState
{
    public required string WorkspaceName { get; init; }
    public required string WorkspacePath { get; init; }
    public required string Source { get; init; }
    public string? SessionId { get; init; }
    public IReadOnlySet<string> VisibleTurnIds { get; init; } = new HashSet<string>(StringComparer.Ordinal);
    public int SelectedTurnCount { get; init; }
    public int ContextCount { get; init; }
    public string HandoffTitle { get; init; } = string.Empty;
    public WorkspaceSnapshotGit Git { get; init; } = new();
}

public sealed record SnapshotComparisonItem(string Label, string SnapshotValue, string CurrentValue, string Status);
public sealed record SnapshotTurnPreviewItem(string Source, string SessionId, string Role, string TurnId, string Status);
public sealed record SnapshotContextPreviewItem(string Type, string Title, string ReferencePath, int CharacterCount);
public sealed record SnapshotHandoffPreview(
    string Target,
    string Template,
    string Title,
    string Task,
    string CurrentState,
    string Constraints,
    string NextAction,
    string IncludeContextPack,
    string IncludeWorkspace,
    string IncludeGitState,
    string IncludeProjectFileSummary);

public sealed record SnapshotGitPreview(
    string SnapshotBranch,
    string SnapshotHead,
    int SnapshotChangedFileCount,
    string SnapshotFingerprint,
    string CurrentBranch,
    string CurrentHead,
    int CurrentChangedFileCount,
    string CurrentFingerprint,
    string Result,
    IReadOnlyList<string> Reasons);

public sealed record SnapshotPreviewModel
{
    public required string Name { get; init; }
    public required string CreatedAt { get; init; }
    public required string UpdatedAt { get; init; }
    public required string WorkspaceName { get; init; }
    public required string WorkspacePath { get; init; }
    public required string SnapshotId { get; init; }
    public required string SchemaVersion { get; init; }
    public int SelectionCount { get; init; }
    public int ContextCount { get; init; }
    public required string HandoffTitle { get; init; }
    public required string GitBranch { get; init; }
    public bool CanRestore { get; init; }
    public required string WorkspaceAffinityMessage { get; init; }
    public IReadOnlyList<SnapshotComparisonItem> Comparisons { get; init; } = [];
    public IReadOnlyList<SnapshotTurnPreviewItem> Turns { get; init; } = [];
    public IReadOnlyList<SnapshotContextPreviewItem> ContextItems { get; init; } = [];
    public required SnapshotHandoffPreview Handoff { get; init; }
    public required SnapshotGitPreview Git { get; init; }
}

public static class SnapshotPreviewBuilder
{
    public static SnapshotPreviewModel Build(WorkspaceSnapshot snapshot, SnapshotPreviewCurrentState current)
    {
        var workspaceMatch = WorkspaceSnapshotService.IsWorkspaceMatch(snapshot, current.WorkspacePath);
        var turns = snapshot.Selection.Select(item => new SnapshotTurnPreviewItem(
            item.Source,
            ShortHash(item.SessionId, 12),
            UiStrings.DisplayRole(item.Role),
            ShortHash(item.TurnId, 12),
            string.Equals(item.Source, current.Source, StringComparison.Ordinal)
                && string.Equals(item.SessionId, current.SessionId, StringComparison.Ordinal)
                && current.VisibleTurnIds.Contains(item.TurnId)
                    ? UiStrings.Matchable
                    : UiStrings.NotCurrentlyMatchable)).ToArray();
        var contexts = snapshot.ContextItems.OrderBy(item => item.Order).Select(item => new SnapshotContextPreviewItem(
            UiStrings.DisplayContextType(item.Type),
            UiStrings.DisplayContextTitle(item.Title),
            item.ReferencePath ?? string.Empty,
            item.Content.Length)).ToArray();
        var gitComparable = IsGitComparable(snapshot.Git, current.Git);
        var gitWarning = gitComparable ? WorkspaceSnapshotService.CompareGit(snapshot.Git, current.Git) : null;
        IReadOnlyList<string> gitReasons = gitComparable ? BuildGitReasons(snapshot.Git, current.Git) : [UiStrings.GitUnavailableReason];

        return new SnapshotPreviewModel
        {
            Name = snapshot.Name,
            CreatedAt = snapshot.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            UpdatedAt = snapshot.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
            WorkspaceName = snapshot.Workspace.Name,
            WorkspacePath = snapshot.Workspace.Path,
            SnapshotId = snapshot.SnapshotId,
            SchemaVersion = snapshot.SchemaVersion.ToString(),
            SelectionCount = snapshot.Selection.Count,
            ContextCount = snapshot.ContextItems.Count,
            HandoffTitle = string.IsNullOrWhiteSpace(snapshot.Handoff.Title) ? UiStrings.NotSet : snapshot.Handoff.Title,
            GitBranch = Display(snapshot.Git.Branch),
            CanRestore = workspaceMatch,
            WorkspaceAffinityMessage = workspaceMatch ? string.Empty : UiStrings.SnapshotWorkspaceMismatchRestoreBlocked,
            Comparisons =
            [
                new(UiStrings.WorkspaceLabel, snapshot.Workspace.Name, current.WorkspaceName, workspaceMatch ? UiStrings.Same : UiStrings.WorkspaceMismatch),
                Compare(UiStrings.GitBranchLabel, snapshot.Git.Branch, current.Git.Branch, UiStrings.Changed),
                Compare(UiStrings.GitHeadLabel, snapshot.Git.HeadCommit, current.Git.HeadCommit, UiStrings.Changed, 8),
                Compare(UiStrings.GitWorkspaceStateLabel, snapshot.Git.StatusFingerprint, current.Git.StatusFingerprint, UiStrings.Changed, 12),
                Replace(UiStrings.SelectedTurnsLabel, snapshot.Selection.Count, current.SelectedTurnCount),
                Replace(UiStrings.ContextLabel, snapshot.ContextItems.Count, current.ContextCount),
                Replace(UiStrings.HandoffTitleLabel, snapshot.Handoff.Title, current.HandoffTitle),
            ],
            Turns = turns,
            ContextItems = contexts,
            Handoff = new SnapshotHandoffPreview(
                snapshot.Handoff.Target,
                UiStrings.HandoffTemplateName(snapshot.Handoff.Template),
                Display(snapshot.Handoff.Title),
                Display(snapshot.Handoff.Task),
                Display(snapshot.Handoff.CurrentState),
                Display(snapshot.Handoff.Constraints),
                Display(snapshot.Handoff.NextAction),
                Included(snapshot.Handoff.IncludeContextPack),
                Included(snapshot.Handoff.IncludeWorkspace),
                Included(snapshot.Handoff.IncludeGitState),
                Included(snapshot.Handoff.IncludeProjectFileSummary)),
            Git = new SnapshotGitPreview(
                Display(snapshot.Git.Branch),
                ShortHash(snapshot.Git.HeadCommit, 8),
                snapshot.Git.ChangedFiles.Count,
                ShortHash(snapshot.Git.StatusFingerprint, 12),
                Display(current.Git.Branch),
                ShortHash(current.Git.HeadCommit, 8),
                current.Git.ChangedFiles.Count,
                ShortHash(current.Git.StatusFingerprint, 12),
                !gitComparable ? UiStrings.Unavailable : gitWarning is null ? UiStrings.Same : UiStrings.CurrentGitDiffers,
                gitReasons),
        };
    }

    private static SnapshotComparisonItem Compare(string label, string saved, string current, string mismatchStatus, int? shortLength = null) =>
        string.IsNullOrWhiteSpace(saved) || string.IsNullOrWhiteSpace(current)
            ? new(label, shortLength is null ? Display(saved) : ShortHash(saved, shortLength.Value), shortLength is null ? Display(current) : ShortHash(current, shortLength.Value), UiStrings.Unavailable)
            : new(label, shortLength is null ? saved : ShortHash(saved, shortLength.Value), shortLength is null ? current : ShortHash(current, shortLength.Value), string.Equals(saved, current, StringComparison.Ordinal) ? UiStrings.Same : mismatchStatus);

    private static SnapshotComparisonItem Replace(string label, int saved, int current) =>
        new(label, saved.ToString(), current.ToString(), saved == current ? UiStrings.Same : UiStrings.WillReplace);

    private static SnapshotComparisonItem Replace(string label, string saved, string current) =>
        new(label, Display(saved), Display(current), string.Equals(saved, current, StringComparison.Ordinal) ? UiStrings.Same : UiStrings.WillReplace);

    private static IReadOnlyList<string> BuildGitReasons(WorkspaceSnapshotGit saved, WorkspaceSnapshotGit current)
    {
        var reasons = new List<string>();
        if (!string.Equals(saved.Branch, current.Branch, StringComparison.Ordinal)) reasons.Add(UiStrings.GitBranchChanged);
        if (!string.Equals(saved.HeadCommit, current.HeadCommit, StringComparison.Ordinal)) reasons.Add(UiStrings.GitHeadChanged);
        if (!string.Equals(saved.StatusFingerprint, current.StatusFingerprint, StringComparison.Ordinal)) reasons.Add(UiStrings.GitWorkspaceStateChanged);
        return reasons;
    }

    private static bool IsGitComparable(WorkspaceSnapshotGit saved, WorkspaceSnapshotGit current) =>
        !string.IsNullOrWhiteSpace(saved.Branch)
        && !string.IsNullOrWhiteSpace(saved.HeadCommit)
        && !string.IsNullOrWhiteSpace(saved.StatusFingerprint)
        && !string.IsNullOrWhiteSpace(current.Branch)
        && !string.IsNullOrWhiteSpace(current.HeadCommit)
        && !string.IsNullOrWhiteSpace(current.StatusFingerprint);

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? UiStrings.NotSet : value;
    private static string Included(bool value) => value ? UiStrings.Yes : UiStrings.No;
    public static string ShortHash(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return UiStrings.Unavailable;
        return value.Length <= length ? value : $"{value[..length]}…";
    }
}
