using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace CodexBridge.App.Infrastructure;

public sealed record ClipboardCopyResult(bool Succeeded, Exception? Error);

public sealed class ClipboardService
{
    public const int ClipboardCantOpenHResult = unchecked((int)0x800401D0);

    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(200),
    ];

    private static readonly TimeSpan[] VerificationDelays =
    [
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(75),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(150),
    ];

    private readonly Action<string> writeText;
    private readonly Func<string?> readText;
    private readonly Func<TimeSpan, Task> delay;
    private readonly Func<Dispatcher?> dispatcherProvider;

    public static ClipboardService Shared { get; } = new(
        Clipboard.SetText,
        Task.Delay,
        Clipboard.GetText,
        () => Application.Current?.Dispatcher);

    public ClipboardService(
        Action<string> writeText,
        Func<TimeSpan, Task>? delay = null,
        Func<string?>? readText = null)
        : this(writeText, delay ?? Task.Delay, readText ?? (static () => null), static () => null)
    {
    }

    private ClipboardService(
        Action<string> writeText,
        Func<TimeSpan, Task> delay,
        Func<string?> readText,
        Func<Dispatcher?> dispatcherProvider)
    {
        this.writeText = writeText;
        this.delay = delay;
        this.readText = readText;
        this.dispatcherProvider = dispatcherProvider;
    }

    public async Task<ClipboardCopyResult> CopyTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            try
            {
                await WriteTextAsync(text);
                return new ClipboardCopyResult(true, null);
            }
            catch (COMException exception) when (exception.HResult == ClipboardCantOpenHResult)
            {
                if (await VerifyExpectedTextAsync(text))
                {
                    return new ClipboardCopyResult(true, null);
                }

                if (attempt == RetryDelays.Length)
                {
                    return new ClipboardCopyResult(false, exception);
                }

                try
                {
                    await delay(RetryDelays[attempt]);
                }
                catch (Exception delayException)
                {
                    return new ClipboardCopyResult(false, delayException);
                }
            }
            catch (Exception exception)
            {
                return new ClipboardCopyResult(false, exception);
            }
        }

        throw new InvalidOperationException("Clipboard retry loop completed unexpectedly.");
    }

    private async Task WriteTextAsync(string text)
    {
        var dispatcher = dispatcherProvider();
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            writeText(text);
            return;
        }

        await dispatcher.InvokeAsync(() => writeText(text));
    }

    private async Task<bool> VerifyExpectedTextAsync(string expectedText)
    {
        for (var attempt = 0; attempt < VerificationDelays.Length; attempt++)
        {
            try
            {
                var dispatcher = dispatcherProvider();
                var actualText = dispatcher is null || dispatcher.CheckAccess()
                    ? readText()
                    : await dispatcher.InvokeAsync(readText);
                if (string.Equals(actualText, expectedText, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (COMException exception) when (exception.HResult == ClipboardCantOpenHResult)
            {
                // The clipboard can remain temporarily busy after a write-side effect.
            }
            catch
            {
                return false;
            }

            if (attempt < VerificationDelays.Length - 1)
            {
                try
                {
                    await delay(VerificationDelays[attempt]);
                }
                catch
                {
                    return false;
                }
            }
        }

        return false;
    }
}
