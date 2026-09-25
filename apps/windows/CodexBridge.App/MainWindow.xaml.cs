using System.Windows;
using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Specialized;
using CodexBridge.App.ViewModels;
using CodexBridge.App.Infrastructure;

namespace CodexBridge.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly ICodexAppLog uiLog;
    private WindowSettings windowSettings;
    private string themeMode;

    public MainWindow(MainWindowViewModel viewModel, ICodexAppLog? uiLog = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.uiLog = uiLog ?? new CodexAppLog(Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "Logs", "ui.log"));
        windowSettings = WindowSettingsStore.Load();
        themeMode = windowSettings.Theme;
        ThemeManager.Apply(themeMode);
        RestoreWindowSettings();
        DraftPanelControl.IsExpanded = windowSettings.DraftExpanded;
        this.uiLog.Write("window startup; settings loaded");
        DataContext = viewModel;
        viewModel.Turns.CollectionChanged += OnTurnsChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await viewModel.InitializeAsync();
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        SaveWindowSettings();
        await viewModel.DisposeAsync();
    }

    private void RestoreWindowSettings()
    {
        Width = Math.Max(MinWidth, windowSettings.Width);
        Height = Math.Max(MinHeight, windowSettings.Height);
        if (windowSettings.Left is not null) Left = windowSettings.Left.Value;
        if (windowSettings.Top is not null) Top = windowSettings.Top.Value;
        LeftPanelColumn.Width = new GridLength(Math.Max(220, windowSettings.LeftPanelWidth));
        RightPanelColumn.Width = new GridLength(Math.Max(300, windowSettings.RightPanelWidth));
    }

    private void SaveWindowSettings()
    {
        try
        {
            windowSettings = new WindowSettings(Width, Height, double.IsNaN(Left) ? null : Left, double.IsNaN(Top) ? null : Top, LeftPanelColumn.ActualWidth, RightPanelColumn.ActualWidth, themeMode, DraftPanelControl.IsExpanded);
            WindowSettingsStore.Save(windowSettings);
            uiLog.Write("window settings saved");
        }
        catch (Exception exception)
        {
            uiLog.Write($"window settings save exception; type={exception.GetType().FullName}; message={exception.Message}");
        }
    }

    private void OpenSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new SettingsViewModel(viewModel, viewModel.RefreshCodexAsync, themeMode);
            var window = new Window
            {
                Title = "Codex Bridge Settings",
                Width = 520,
                Height = 620,
                Owner = this,
                Content = new SettingsView { DataContext = settings },
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            window.Closed += (_, _) => { themeMode = settings.ThemeMode; uiLog.Write("settings window closed"); };
            window.ShowDialog();
        }
        catch (Exception exception)
        {
            uiLog.Write($"settings exception; type={exception.GetType().FullName}; message={exception.Message}");
        }
    }

    private void OpenWorkspaceClick(object sender, RoutedEventArgs e)
    {
        var window = new Window
        {
            Title = "Workspace Explorer", Width = 620, Height = 560, Owner = this,
            Content = new WorkspaceView { DataContext = viewModel },
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.ShowDialog();
    }

    private void OnTurnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() => ConversationScrollViewer.ScrollToEnd());
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        switch (e.Key)
        {
            case Key.R:
                if (viewModel.RefreshActiveCommand.CanExecute(null)) viewModel.RefreshActiveCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.K:
                SessionListControl.SearchBoxControl.Focus();
                e.Handled = true;
                break;
            case Key.C:
                if (Keyboard.FocusedElement is not TextBox && viewModel.CopyDraftCommand.CanExecute(null))
                {
                    viewModel.CopyDraftCommand.Execute(null);
                    e.Handled = true;
                }
                break;
            case Key.D1:
                viewModel.SourceIndex = 0;
                e.Handled = true;
                break;
            case Key.D2:
                viewModel.SourceIndex = 1;
                e.Handled = true;
                break;
        }
    }
}
