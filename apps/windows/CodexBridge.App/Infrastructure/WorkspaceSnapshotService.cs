using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;

namespace CodexBridge.App.Infrastructure;

public sealed class WorkspaceSnapshotService
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string GetRoot(string workspacePath) => Path.Combine(workspacePath, ".ai", "snapshots");

    public WorkspaceSnapshot Save(string workspacePath, WorkspaceSnapshot snapshot)
    {
        Validate(snapshot);
        var directory = Path.Combine(GetRoot(workspacePath), snapshot.SnapshotId);
        Directory.CreateDirectory(directory);
        WriteAtomic(Path.Combine(directory, "snapshot.json"), JsonSerializer.Serialize(snapshot, JsonOptions));
        WriteAtomic(Path.Combine(directory, "summary.md"), RenderSummary(snapshot));
        return snapshot;
    }

    public IReadOnlyList<WorkspaceSnapshotEntry> List(string workspacePath)
    {
        var root = GetRoot(workspacePath);
        if (!Directory.Exists(root)) return [];
        return Directory.EnumerateDirectories(root).OrderByDescending(path => Directory.GetCreationTimeUtc(path)).Select(path =>
        {
            try
            {
                var snapshotPath = Path.Combine(path, "snapshot.json");
                if (!File.Exists(snapshotPath)) return new WorkspaceSnapshotEntry(path, null, "snapshot.json not found");
                var snapshot = JsonSerializer.Deserialize<WorkspaceSnapshot>(File.ReadAllText(snapshotPath), JsonOptions);
                if (snapshot is null) return new WorkspaceSnapshotEntry(path, null, "empty snapshot");
                Validate(snapshot);
                return new WorkspaceSnapshotEntry(path, snapshot, null);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException)
            {
                return new WorkspaceSnapshotEntry(path, null, exception.Message);
            }
        }).ToArray();
    }

    public WorkspaceSnapshot Load(string directoryPath)
    {
        var snapshot = JsonSerializer.Deserialize<WorkspaceSnapshot>(File.ReadAllText(Path.Combine(directoryPath, "snapshot.json")), JsonOptions);
        if (snapshot is null) throw new InvalidDataException("empty snapshot");
        Validate(snapshot);
        return snapshot;
    }

    public WorkspaceSnapshot Rename(string workspacePath, string snapshotId, string name)
    {
        var directory = Path.Combine(GetRoot(workspacePath), snapshotId);
        var snapshot = Load(directory) with { Name = name, UpdatedAt = DateTimeOffset.Now };
        return Save(workspacePath, snapshot);
    }

    public WorkspaceSnapshot ImportValidatedSnapshot(string workspacePath, WorkspaceSnapshot snapshot)
    {
        Validate(snapshot);
        if (!Path.IsPathFullyQualified(workspacePath)
            || string.IsNullOrWhiteSpace(snapshot.SnapshotId)
            || snapshot.SnapshotId is "." or ".."
            || snapshot.SnapshotId.Length > 128
            || snapshot.SnapshotId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
        }

        var root = GetRoot(workspacePath);
        Directory.CreateDirectory(root);
        var target = Path.Combine(root, snapshot.SnapshotId);
        if (Directory.Exists(target) || File.Exists(target)) throw new InvalidDataException(UiStrings.SnapshotAlreadyExists);
        var stagingRoot = Path.Combine(workspacePath, ".ai", $".snapshot-import-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(stagingRoot);
            WriteAtomic(Path.Combine(stagingRoot, "snapshot.json"), JsonSerializer.Serialize(snapshot, JsonOptions));
            WriteAtomic(Path.Combine(stagingRoot, "summary.md"), RenderSummary(snapshot));
            Directory.Move(stagingRoot, target);
            return snapshot;
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
        }
    }

    public void Delete(string workspacePath, string snapshotId)
    {
        DeleteDirectory(workspacePath, Path.Combine(GetRoot(workspacePath), snapshotId));
    }

    public void DeleteDirectory(string workspacePath, string directoryPath)
    {
        var root = Path.GetFullPath(GetRoot(workspacePath)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Path.GetFullPath(directoryPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(directory);
        if (string.IsNullOrWhiteSpace(parent) || !string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("工作现场删除路径无效。");
        }

        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    public static void Validate(WorkspaceSnapshot snapshot)
    {
        if (snapshot is null || snapshot.Workspace is null || snapshot.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException(snapshot is not null && snapshot.SchemaVersion > CurrentSchemaVersion ? "此工作现场来自更新版本的 Codex Bridge。" : "不支持此工作现场版本。");
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotId) || string.IsNullOrWhiteSpace(snapshot.Workspace.Path)) throw new InvalidDataException("工作现场数据不完整。");
    }

    public static bool IsWorkspaceMatch(WorkspaceSnapshot snapshot, string workspacePath) =>
        string.Equals(Path.GetFullPath(snapshot.Workspace.Path).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    public static string? CompareGit(WorkspaceSnapshotGit saved, WorkspaceSnapshotGit current)
    {
        if (!string.Equals(saved.Branch, current.Branch, StringComparison.Ordinal)) return $"保存时分支：{saved.Branch}，当前分支：{current.Branch}";
        if (!string.Equals(saved.HeadCommit, current.HeadCommit, StringComparison.Ordinal)) return $"保存时 HEAD：{saved.HeadCommit}，当前 HEAD：{current.HeadCommit}";
        if (!string.Equals(saved.StatusFingerprint, current.StatusFingerprint, StringComparison.Ordinal)) return "工作区文件状态已变化。";
        return null;
    }

    public static string BuildStatusFingerprint(string branch, string headCommit, IEnumerable<WorkspaceSnapshotChangedFile> files)
    {
        var canonical = string.Join("\n", new[] { branch, headCommit }.Concat(files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).Select(file => $"{file.Status} {file.RelativePath}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static string RenderSummary(WorkspaceSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# 工作现场").AppendLine().AppendLine($"名称：{snapshot.Name}").AppendLine($"保存时间：{snapshot.CreatedAt:yyyy-MM-dd HH:mm}").AppendLine($"更新时间：{snapshot.UpdatedAt:yyyy-MM-dd HH:mm}").AppendLine($"工作区：{snapshot.Workspace.Name}").AppendLine();
        builder.AppendLine("## 当前任务").AppendLine().AppendLine(snapshot.Handoff.Task).AppendLine().AppendLine("## 当前状态").AppendLine().AppendLine(snapshot.Handoff.CurrentState).AppendLine().AppendLine("## 下一步").AppendLine().AppendLine(snapshot.Handoff.NextAction).AppendLine();
        builder.AppendLine("## 上下文").AppendLine().AppendLine($"{snapshot.ContextItems.Count} 项").AppendLine().AppendLine("## Git").AppendLine().AppendLine($"分支：{snapshot.Git.Branch}").AppendLine($"HEAD：{snapshot.Git.HeadCommit}").AppendLine($"变更文件：{snapshot.Git.ChangedFiles.Count}").AppendLine().AppendLine("## 交接").AppendLine().AppendLine($"目标：{snapshot.Handoff.Target}").AppendLine($"模板：{UiStrings.HandoffTemplateName(snapshot.Handoff.Template)}");
        return builder.ToString();
    }

    private static void WriteAtomic(string path, string content)
    {
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
                writer.Flush();
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
