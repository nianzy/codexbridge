using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexBridge.App.Infrastructure;

public sealed record SnapshotAutoIndexDocument
{
    public int SchemaVersion { get; init; } = 1;
    public List<string> SnapshotIds { get; init; } = [];
}

public sealed record SnapshotAutoIndexReadResult(IReadOnlySet<string> SnapshotIds, bool IsCorrupt);

public sealed class SnapshotAutoIndexService
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string GetPath(string workspacePath) => Path.Combine(workspacePath, ".ai", "snapshot-auto-index.json");

    public SnapshotAutoIndexReadResult Read(string workspacePath)
    {
        var path = GetPath(workspacePath);
        if (!File.Exists(path)) return new(new HashSet<string>(StringComparer.Ordinal), false);
        try
        {
            var document = JsonSerializer.Deserialize<SnapshotAutoIndexDocument>(File.ReadAllText(path), JsonOptions);
            if (document is null || document.SchemaVersion != CurrentSchemaVersion || document.SnapshotIds is null || document.SnapshotIds.Any(string.IsNullOrWhiteSpace))
                return new(new HashSet<string>(StringComparer.Ordinal), true);
            return new(document.SnapshotIds.ToHashSet(StringComparer.Ordinal), false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(new HashSet<string>(StringComparer.Ordinal), true);
        }
    }

    public bool IsAuto(string workspacePath, string snapshotId) => Read(workspacePath).SnapshotIds.Contains(snapshotId);

    public bool MarkAuto(string workspacePath, string snapshotId)
    {
        var current = Read(workspacePath);
        if (current.IsCorrupt) return false;
        var ids = current.SnapshotIds.ToHashSet(StringComparer.Ordinal);
        if (!ids.Add(snapshotId)) return true;
        Write(workspacePath, ids);
        return true;
    }

    public bool Remove(string workspacePath, string snapshotId)
    {
        var current = Read(workspacePath);
        if (current.IsCorrupt || !current.SnapshotIds.Contains(snapshotId)) return !current.IsCorrupt;
        var ids = current.SnapshotIds.ToHashSet(StringComparer.Ordinal);
        ids.Remove(snapshotId);
        Write(workspacePath, ids);
        return true;
    }

    public bool PruneMissingEntries(string workspacePath)
    {
        var current = Read(workspacePath);
        if (current.IsCorrupt) return false;
        var existing = current.SnapshotIds
            .Where(id => Directory.Exists(Path.Combine(WorkspaceSnapshotService.GetRoot(workspacePath), id)))
            .ToHashSet(StringComparer.Ordinal);
        if (existing.SetEquals(current.SnapshotIds)) return true;
        Write(workspacePath, existing);
        return true;
    }

    private static void Write(string workspacePath, IEnumerable<string> ids)
    {
        var directory = Path.Combine(workspacePath, ".ai");
        Directory.CreateDirectory(directory);
        var path = GetPath(workspacePath);
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                var document = new SnapshotAutoIndexDocument { SnapshotIds = ids.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList() };
                writer.Write(JsonSerializer.Serialize(document, JsonOptions));
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
