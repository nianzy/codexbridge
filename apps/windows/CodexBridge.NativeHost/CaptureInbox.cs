using CodexBridge.Core.Capture;

namespace CodexBridge.NativeHost;

public sealed class CaptureInbox
{
    public CaptureInbox(string? directoryPath = null)
    {
        DirectoryPath = directoryPath
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Codex Bridge",
                "CaptureInbox");
    }

    public string DirectoryPath { get; }

    public string Store(CapturePayload payload)
    {
        Directory.CreateDirectory(DirectoryPath);
        var name = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():D}";
        var temporaryPath = Path.Combine(DirectoryPath, $".{name}.tmp");
        var destinationPath = Path.Combine(DirectoryPath, $"{name}.json");
        var data = CaptureJson.Serialize(payload);

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 16 * 1024,
                       options: FileOptions.WriteThrough))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, destinationPath);
            return destinationPath;
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }
}
