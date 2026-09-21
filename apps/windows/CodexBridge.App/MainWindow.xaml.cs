using System.Windows;
using CodexBridge.App.ViewModels;

namespace CodexBridge.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.InitializeAsync();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        await viewModel.DisposeAsync();
    }
}
