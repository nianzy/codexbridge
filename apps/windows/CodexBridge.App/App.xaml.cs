using System.Windows;
using CodexBridge.App.Infrastructure;
using CodexBridge.App.ViewModels;

namespace CodexBridge.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var repository = new SqliteConversationRepository();
        var importer = new CaptureInboxImporter(repository);
        var viewModel = new MainWindowViewModel(repository, importer);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }
}
