using System.IO;
using System.Windows;
using CodexBridge.App.Infrastructure;
using CodexBridge.App.ViewModels;
using CodexBridge.Core.Codex;

namespace CodexBridge.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--codex-only-probe", StringComparer.OrdinalIgnoreCase))
        {
            await RunCodexOnlyProbeAsync();
            return;
        }

        var repository = new SqliteConversationRepository();
        var importer = new CaptureInboxImporter(repository);
        var codexLog = new CodexAppLog();
        var uiLog = new CodexAppLog(Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "Logs", "ui.log"));
        codexLog.Write($"process workingDirectory={Environment.CurrentDirectory}");
        codexLog.Write($"process USERPROFILE={Environment.GetEnvironmentVariable("USERPROFILE") ?? "(unset)"}");
        codexLog.Write($"process CODEX_HOME set={Environment.GetEnvironmentVariable("CODEX_HOME") is not null}");
        codexLog.Write($"process arguments={string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(argument => argument.Contains(' ') ? "(arg)" : argument))}");
        var codexClient = new CodexAppServerClient(diagnostics: codexLog.Write);
        var viewModel = new MainWindowViewModel(
            repository,
            importer,
            codexClient,
            codexLog,
            () => new CodexAppServerClient(diagnostics: codexLog.Write),
            uiLog);
        var window = new MainWindow(viewModel, uiLog);
        MainWindow = window;
        window.Show();
    }

    private async Task RunCodexOnlyProbeAsync()
    {
        var logPath = Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "Logs", "codex-only-probe.log");
        var log = new CodexAppLog(logPath);
        log.Write($"process workingDirectory={Environment.CurrentDirectory}");
        log.Write($"process arguments={string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(argument => argument.Contains(' ') ? "(arg)" : argument))}");
        await using var client = new CodexAppServerClient(diagnostics: log.Write);
        try
        {
            var probe = await client.ProbeAsync();
            log.Write($"probe codexHome={probe.Initialize.CodexHome ?? "(null)"}");
            var threads = await client.ListThreadsAsync(100);
            log.Write($"probe thread/list success; thread count={threads.Count}");
            await client.DisposeAsync();
            Shutdown(0);
        }
        catch (Exception exception)
        {
            log.Write($"probe exception type={exception.GetType().FullName} message={exception.Message} inner={exception.InnerException?.Message ?? "(none)"}");
            Shutdown(1);
        }
    }
}
