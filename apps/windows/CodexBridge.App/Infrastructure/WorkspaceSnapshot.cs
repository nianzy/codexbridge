using System.Text.Json.Serialization;

namespace CodexBridge.App.Infrastructure;

public sealed record WorkspaceSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public required string SnapshotId { get; init; }
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string AppVersion { get; init; } = ProductInfo.Version;
    public required WorkspaceSnapshotWorkspace Workspace { get; init; }
    public List<WorkspaceSnapshotSelection> Selection { get; init; } = [];
    public List<WorkspaceSnapshotContextItem> ContextItems { get; init; } = [];
    public WorkspaceSnapshotHandoff Handoff { get; init; } = new();
    public WorkspaceSnapshotGit Git { get; init; } = new();
}

public sealed record WorkspaceSnapshotWorkspace
{
    public required string Name { get; init; }
    public required string Path { get; init; }
}

public sealed record WorkspaceSnapshotSelection
{
    public required string Source { get; init; }
    public required string SessionId { get; init; }
    public required string TurnId { get; init; }
    public string? Role { get; init; }
    public string? TextHash { get; init; }
}

public sealed record WorkspaceSnapshotContextItem
{
    public required string Key { get; init; }
    public required string Type { get; init; }
    public required string Title { get; init; }
    public required string Content { get; init; }
    public string? Source { get; init; }
    public string? ReferencePath { get; init; }
    public int Order { get; init; }
}

public sealed record WorkspaceSnapshotHandoff
{
    public string Target { get; init; } = "Codex";
    public string Template { get; init; } = "Continue Task";
    public string Title { get; init; } = string.Empty;
    public string Task { get; init; } = string.Empty;
    public string CurrentState { get; init; } = string.Empty;
    public string Constraints { get; init; } = string.Empty;
    public string NextAction { get; init; } = string.Empty;
    public bool IncludeContextPack { get; init; }
    public bool IncludeWorkspace { get; init; }
    public bool IncludeGitState { get; init; }
    public bool IncludeProjectFileSummary { get; init; }
}

public sealed record WorkspaceSnapshotGit
{
    public string Branch { get; init; } = string.Empty;
    public string HeadCommit { get; init; } = string.Empty;
    public List<WorkspaceSnapshotChangedFile> ChangedFiles { get; init; } = [];
    public string StatusFingerprint { get; init; } = string.Empty;
}

public sealed record WorkspaceSnapshotChangedFile
{
    public required string Status { get; init; }
    public required string RelativePath { get; init; }
}

public sealed record WorkspaceSnapshotEntry(string DirectoryPath, WorkspaceSnapshot? Snapshot, string? Error)
{
    public bool IsAuto { get; init; }
    public string DisplayName => Snapshot?.Name ?? "无法读取";
    public string TypeDisplay => Snapshot is null ? string.Empty : IsAuto ? UiStrings.SnapshotAutomatic : UiStrings.SnapshotManual;
}
