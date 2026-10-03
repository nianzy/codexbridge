namespace CodexBridge.App.Infrastructure;

public sealed record SnapshotPackageManifest
{
    public int PackageVersion { get; init; }
    public required string Product { get; init; }
    public required string SnapshotId { get; init; }
    public int SnapshotSchemaVersion { get; init; }
    public required string SnapshotSha256 { get; init; }
    public DateTimeOffset ExportedAt { get; init; }
    public required string AppVersion { get; init; }
}

public sealed record SnapshotPackageInspection
{
    public required string PackagePath { get; init; }
    public required SnapshotPackageManifest Manifest { get; init; }
    public required WorkspaceSnapshot Snapshot { get; init; }
    public required string CurrentWorkspaceName { get; init; }
    public required string CurrentWorkspacePath { get; init; }
    public bool WorkspaceMatches { get; init; }
    public string SnapshotName => Snapshot.Name;
    public string SnapshotId => Snapshot.SnapshotId;
    public int SchemaVersion => Snapshot.SchemaVersion;
    public string OriginalWorkspaceName => Snapshot.Workspace.Name;
    public string OriginalWorkspacePath => Snapshot.Workspace.Path;
    public int SelectionCount => Snapshot.Selection.Count;
    public int ContextCount => Snapshot.ContextItems.Count;
    public string HandoffTitle => string.IsNullOrWhiteSpace(Snapshot.Handoff.Title) ? UiStrings.NotSet : Snapshot.Handoff.Title;
    public string CreatedAt => Snapshot.CreatedAt.ToString("yyyy-MM-dd HH:mm");
    public string UpdatedAt => Snapshot.UpdatedAt.ToString("yyyy-MM-dd HH:mm");
    public string WorkspaceMismatchWarning => WorkspaceMatches ? string.Empty : UiStrings.SnapshotImportMismatchWarning;
}
