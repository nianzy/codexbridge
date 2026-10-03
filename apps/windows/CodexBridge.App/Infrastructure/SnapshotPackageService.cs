using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexBridge.App.Infrastructure;

public sealed class SnapshotPackageService
{
    public const int CurrentPackageVersion = 1;
    public const string ManifestEntryName = "manifest.json";
    public const string SnapshotEntryName = "snapshot.json";
    public const string SummaryEntryName = "summary.md";
    private const long MaximumEntryBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> AllowedEntries = new(StringComparer.Ordinal)
    {
        ManifestEntryName,
        SnapshotEntryName,
        SummaryEntryName,
    };

    private readonly WorkspaceSnapshotService snapshotService;

    public SnapshotPackageService(WorkspaceSnapshotService? snapshotService = null) =>
        this.snapshotService = snapshotService ?? new WorkspaceSnapshotService();

    public string BuildDefaultFileName(WorkspaceSnapshot snapshot)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(snapshot.Name.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "Snapshot";
        return $"CodexBridge-Snapshot-{sanitized}-{snapshot.SnapshotId}.zip";
    }

    public void Export(string packagePath, WorkspaceSnapshot snapshot, DateTimeOffset? exportedAt = null)
    {
        WorkspaceSnapshotService.Validate(snapshot);
        ValidateSnapshotIdentity(snapshot);
        var snapshotBytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        var summaryBytes = Encoding.UTF8.GetBytes(WorkspaceSnapshotService.RenderSummary(snapshot));
        var manifest = new SnapshotPackageManifest
        {
            PackageVersion = CurrentPackageVersion,
            Product = "Codex Bridge",
            SnapshotId = snapshot.SnapshotId,
            SnapshotSchemaVersion = snapshot.SchemaVersion,
            SnapshotSha256 = Convert.ToHexString(SHA256.HashData(snapshotBytes)),
            ExportedAt = exportedAt ?? DateTimeOffset.Now,
            AppVersion = snapshot.AppVersion,
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        var fullPath = Path.GetFullPath(packagePath);
        var parent = Path.GetDirectoryName(fullPath) ?? throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
        Directory.CreateDirectory(parent);
        var tempPath = Path.Combine(parent, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteEntry(archive, ManifestEntryName, manifestBytes);
                    WriteEntry(archive, SnapshotEntryName, snapshotBytes);
                    WriteEntry(archive, SummaryEntryName, summaryBytes);
                }
                stream.Flush(true);
            }
            File.Move(tempPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public SnapshotPackageInspection InspectImport(string packagePath, string currentWorkspaceName, string currentWorkspacePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(currentWorkspacePath) || !Path.IsPathFullyQualified(currentWorkspacePath))
                throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
            using var stream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry(ManifestEntryName) ?? throw new InvalidDataException(UiStrings.SnapshotPackageMissingManifest);
            var snapshotEntry = archive.GetEntry(SnapshotEntryName) ?? throw new InvalidDataException(UiStrings.SnapshotPackageMissingSnapshot);
            ValidateEntries(archive);
            var manifestBytes = ReadEntryBytes(manifestEntry);
            var snapshotBytes = ReadEntryBytes(snapshotEntry);
            var manifest = DeserializeManifest(manifestBytes);
            ValidatePackageVersion(manifest.PackageVersion);
            ValidateManifest(manifest);
            var snapshot = DeserializeSnapshot(snapshotBytes);
            WorkspaceSnapshotService.Validate(snapshot);
            ValidateSnapshotIdentity(snapshot);
            if (!string.Equals(manifest.Product, "Codex Bridge", StringComparison.Ordinal)) throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
            if (!string.Equals(manifest.SnapshotId, snapshot.SnapshotId, StringComparison.Ordinal)) throw new InvalidDataException(UiStrings.SnapshotPackageIdentityMismatch);
            if (manifest.SnapshotSchemaVersion != snapshot.SchemaVersion) throw new InvalidDataException(UiStrings.SnapshotPackageSchemaMismatch);
            var actualHash = Convert.ToHexString(SHA256.HashData(snapshotBytes));
            if (!string.Equals(actualHash, manifest.SnapshotSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(UiStrings.SnapshotIntegrityFailed);
            var targetDirectory = Path.Combine(WorkspaceSnapshotService.GetRoot(currentWorkspacePath), snapshot.SnapshotId);
            if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory)) throw new InvalidDataException(UiStrings.SnapshotAlreadyExists);
            return new SnapshotPackageInspection
            {
                PackagePath = Path.GetFullPath(packagePath),
                Manifest = manifest,
                Snapshot = snapshot,
                CurrentWorkspaceName = currentWorkspaceName,
                CurrentWorkspacePath = currentWorkspacePath,
                WorkspaceMatches = WorkspaceSnapshotService.IsWorkspaceMatch(snapshot, currentWorkspacePath),
            };
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new InvalidDataException(UiStrings.SnapshotPackageInvalid, exception);
        }
    }

    public WorkspaceSnapshot ImportValidated(SnapshotPackageInspection inspection)
    {
        if (inspection is null) throw new ArgumentNullException(nameof(inspection));
        WorkspaceSnapshotService.Validate(inspection.Snapshot);
        ValidateSnapshotIdentity(inspection.Snapshot);
        return snapshotService.ImportValidatedSnapshot(inspection.CurrentWorkspacePath, inspection.Snapshot);
    }

    private static void ValidateEntries(ZipArchive archive)
    {
        var entries = archive.Entries;
        if (entries.Count != AllowedEntries.Count
            || entries.Any(entry => !string.Equals(entry.FullName, entry.Name, StringComparison.Ordinal) || !AllowedEntries.Contains(entry.FullName))
            || entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != AllowedEntries.Count)
        {
            throw new InvalidDataException(UiStrings.SnapshotPackageUnsupportedFiles);
        }
    }

    private static SnapshotPackageManifest DeserializeManifest(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<SnapshotPackageManifest>(bytes, JsonOptions)
                ?? throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(UiStrings.SnapshotPackageInvalid, exception);
        }
    }

    private static WorkspaceSnapshot DeserializeSnapshot(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<WorkspaceSnapshot>(bytes, JsonOptions)
                ?? throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(UiStrings.SnapshotPackageInvalid, exception);
        }
    }

    private static void ValidatePackageVersion(int version)
    {
        if (version > CurrentPackageVersion) throw new InvalidDataException(UiStrings.SnapshotPackageNewerVersion);
        if (version <= 0 || version != CurrentPackageVersion) throw new InvalidDataException(UiStrings.SnapshotPackageUnsupported);
    }

    private static void ValidateManifest(SnapshotPackageManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Product)
            || string.IsNullOrWhiteSpace(manifest.SnapshotId)
            || string.IsNullOrWhiteSpace(manifest.SnapshotSha256)
            || manifest.SnapshotSha256.Length != 64
            || manifest.SnapshotSha256.Any(character => !Uri.IsHexDigit(character))
            || string.IsNullOrWhiteSpace(manifest.AppVersion))
        {
            throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
        }
    }

    private static void ValidateSnapshotIdentity(WorkspaceSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Workspace.Name)
            || string.IsNullOrWhiteSpace(snapshot.Workspace.Path)
            || !Path.IsPathFullyQualified(snapshot.Workspace.Path)
            || snapshot.SnapshotId.Length > 128
            || snapshot.SnapshotId is "." or ".."
            || snapshot.SnapshotId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
        }
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        if (entry.Length < 0 || entry.Length > MaximumEntryBytes) throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
        using var input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        input.CopyTo(output);
        if (output.Length > MaximumEntryBytes) throw new InvalidDataException(UiStrings.SnapshotPackageInvalid);
        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }
}
