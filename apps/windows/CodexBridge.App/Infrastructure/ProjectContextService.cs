using System.Text.Json;
using System.IO;

namespace CodexBridge.App.Infrastructure;

public sealed record ProjectContextFile(string Path, long Size, DateTimeOffset Modified);
public sealed record ProjectContextSnapshot(string Name, string Path, DateTimeOffset GeneratedAt, IReadOnlyList<ProjectContextFile> Files);

public sealed class ProjectContextService
{
    public static readonly string[] DefaultIgnoreFolders = [".git", "bin", "obj", "node_modules"];

    public ProjectContextSnapshot Build(WorkspaceItem workspace, IEnumerable<string> ignoreFolders)
    {
        var ignored = ignoreFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = System.IO.Path.GetFullPath(workspace.Path);
        var files = Directory.Exists(root)
            ? EnumerateAccessibleFiles(root, ignored)
                .Select(file => new FileInfo(file))
                .Select(file => new ProjectContextFile(System.IO.Path.GetRelativePath(root, file.FullName), file.Length, file.LastWriteTimeUtc))
                .OrderBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];
        return new ProjectContextSnapshot(workspace.Name, root, DateTimeOffset.Now, files);
    }

    private static IEnumerable<string> EnumerateAccessibleFiles(string directory, HashSet<string> ignored)
    {
        IEnumerable<string> files;
        IEnumerable<string> directories;
        try
        {
            files = Directory.EnumerateFiles(directory).ToArray();
            directories = Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            yield break;
        }

        foreach (var file in files) yield return file;
        foreach (var child in directories)
        {
            if (ignored.Contains(System.IO.Path.GetFileName(child))) continue;
            foreach (var file in EnumerateAccessibleFiles(child, ignored)) yield return file;
        }
    }

    public string Write(WorkspaceItem workspace, IEnumerable<string> ignoreFolders)
    {
        var snapshot = Build(workspace, ignoreFolders);
        var directory = System.IO.Path.Combine(workspace.Path, ".ai");
        Directory.CreateDirectory(directory);
        var path = System.IO.Path.Combine(directory, "context.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
