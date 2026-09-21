using System.IO;
using System.Collections.Concurrent;
using System.Threading.Channels;
using CodexBridge.Core.Capture;
using CodexBridge.Core.Conversations;

namespace CodexBridge.App.Infrastructure;

public enum CaptureImportStatus
{
    Imported,
    Skipped,
    Failed,
}

public sealed record CaptureImportResult(
    CaptureImportStatus Status,
    string SourcePath,
    string? DestinationPath,
    string? Error);

public sealed class CaptureImportedEventArgs : EventArgs
{
    public CaptureImportedEventArgs(StoredConversation conversation)
    {
        Conversation = conversation;
    }

    public StoredConversation Conversation { get; }
}

public sealed class CaptureInboxImporter : IAsyncDisposable
{
    private readonly IConversationRepository repository;
    private readonly CaptureValidator validator;
    private readonly string inboxDirectory;
    private readonly string processedDirectory;
    private readonly string failedDirectory;
    private readonly TimeSpan stabilityDelay;
    private readonly SemaphoreSlim importGate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> queuedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private CancellationTokenSource? lifetime;
    private FileSystemWatcher? watcher;
    private Task? worker;

    public CaptureInboxImporter(
        IConversationRepository repository,
        CaptureValidator? validator = null,
        string? inboxDirectory = null,
        TimeSpan? stabilityDelay = null)
    {
        this.repository = repository;
        this.validator = validator ?? new CaptureValidator();
        this.inboxDirectory = inboxDirectory ?? CodexBridgeWindowsPaths.CaptureInboxPath;
        processedDirectory = Path.Combine(this.inboxDirectory, "processed");
        failedDirectory = Path.Combine(this.inboxDirectory, "failed");
        this.stabilityDelay = stabilityDelay ?? TimeSpan.FromMilliseconds(100);
    }

    public event EventHandler<CaptureImportedEventArgs>? Imported;

    public string InboxDirectory => inboxDirectory;

    public string ProcessedDirectory => processedDirectory;

    public string FailedDirectory => failedDirectory;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (lifetime is not null)
        {
            return;
        }

        await repository.InitializeAsync(cancellationToken);
        Directory.CreateDirectory(inboxDirectory);
        Directory.CreateDirectory(processedDirectory);
        Directory.CreateDirectory(failedDirectory);

        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        worker = Task.Run(() => ProcessQueueAsync(lifetime.Token), CancellationToken.None);
        watcher = CreateWatcher();

        foreach (var path in Directory.EnumerateFiles(inboxDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            await ImportQueuedPathAsync(path, cancellationToken);
        }
    }

    public async Task<CaptureImportResult> ImportFileAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        await importGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(fullPath))
            {
                return new CaptureImportResult(CaptureImportStatus.Skipped, fullPath, null, null);
            }

            try
            {
                var capture = await ReadAndValidateAsync(fullPath, cancellationToken);
                await repository.SaveAsync(capture, cancellationToken);
                var conversation = await repository.GetAsync(capture.ConversationId, cancellationToken)
                    ?? throw new InvalidDataException("Imported conversation could not be read back from SQLite.");
                var processedPath = await MoveWithRetryAsync(fullPath, processedDirectory, cancellationToken);
                Imported?.Invoke(this, new CaptureImportedEventArgs(conversation));
                return new CaptureImportResult(
                    CaptureImportStatus.Imported,
                    fullPath,
                    processedPath,
                    null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var failedPath = await MoveToFailedAsync(fullPath, exception, cancellationToken);
                return new CaptureImportResult(
                    CaptureImportStatus.Failed,
                    fullPath,
                    failedPath,
                    exception.Message);
            }
        }
        finally
        {
            importGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        var currentLifetime = lifetime;
        if (currentLifetime is null)
        {
            return;
        }

        currentLifetime.Cancel();
        watcher?.Dispose();
        queue.Writer.TryComplete();
        if (worker is not null)
        {
            try
            {
                await worker;
            }
            catch (OperationCanceledException)
            {
            }
        }

        currentLifetime.Dispose();
        lifetime = null;
        worker = null;
        watcher = null;
    }

    private FileSystemWatcher CreateWatcher()
    {
        var result = new FileSystemWatcher(inboxDirectory, "*.json")
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        result.Created += (_, args) => Enqueue(args.FullPath);
        result.Changed += (_, args) => Enqueue(args.FullPath);
        result.Renamed += (_, args) => Enqueue(args.FullPath);
        result.Error += (_, _) => EnqueueExistingFiles();
        return result;
    }

    private void Enqueue(string path)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || !queuedPaths.TryAdd(Path.GetFullPath(path), 0))
        {
            return;
        }

        queue.Writer.TryWrite(Path.GetFullPath(path));
    }

    private void EnqueueExistingFiles()
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(inboxDirectory, "*.json", SearchOption.TopDirectoryOnly))
            {
                Enqueue(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        await foreach (var path in queue.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ImportFileAsync(path, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                queuedPaths.TryRemove(path, out _);
            }
        }
    }

    private async Task ImportQueuedPathAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!queuedPaths.TryAdd(fullPath, 0))
        {
            return;
        }

        try
        {
            await ImportFileAsync(fullPath, cancellationToken);
        }
        finally
        {
            queuedPaths.TryRemove(fullPath, out _);
        }
    }

    private async Task<CaptureValidationResult> ReadAndValidateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = new FileInfo(path);
            await Task.Delay(stabilityDelay, cancellationToken);
            var after = new FileInfo(path);
            if (before.Length != after.Length || before.LastWriteTimeUtc != after.LastWriteTimeUtc)
            {
                continue;
            }

            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 16 * 1024,
                    options: FileOptions.SequentialScan);
                if (stream.Length > CaptureValidator.MaximumPayloadBytes)
                {
                    throw new CaptureValidationException(
                        CaptureValidationErrorCode.Oversized,
                        "Capture payload exceeds the 2 MiB limit.");
                }

                var bytes = new byte[checked((int)stream.Length)];
                await stream.ReadExactlyAsync(bytes, cancellationToken);
                return validator.Validate(bytes);
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(stabilityDelay, cancellationToken);
            }
            catch (CaptureValidationException exception)
                when (exception.Code == CaptureValidationErrorCode.InvalidPayload && attempt < 2)
            {
                await Task.Delay(stabilityDelay, cancellationToken);
            }
        }

        throw new IOException("Capture file did not become stable before the import timeout.");
    }

    private async Task<string> MoveWithRetryAsync(
        string sourcePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destinationPath = UniqueDestinationPath(destinationDirectory, Path.GetFileName(sourcePath));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(sourcePath, destinationPath);
                return destinationPath;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(stabilityDelay, cancellationToken);
            }
        }
    }

    private async Task<string?> MoveToFailedAsync(
        string sourcePath,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            var failedPath = await MoveWithRetryAsync(sourcePath, failedDirectory, cancellationToken);
            var reasonPath = failedPath + ".error.txt";
            await File.WriteAllTextAsync(reasonPath, exception.ToString(), cancellationToken);
            return failedPath;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string UniqueDestinationPath(string directory, string fileName)
    {
        var candidate = Path.Combine(directory, fileName);
        return File.Exists(candidate)
            ? Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}.json")
            : candidate;
    }
}
