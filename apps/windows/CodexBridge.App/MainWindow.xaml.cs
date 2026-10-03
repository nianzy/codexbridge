using System.Windows;
using System.IO;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
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
    private bool suppressWorkspaceComboBoxSelection;
    private bool isExitConfirmed;
    private bool exitPromptInProgress;

    public MainWindow(MainWindowViewModel viewModel, ICodexAppLog? uiLog = null)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.uiLog = uiLog ?? new CodexAppLog(Path.Combine(CodexBridgeWindowsPaths.SupportDirectory, "Logs", "ui.log"));
        windowSettings = WindowSettingsStore.Load();
        viewModel.ApplyWorkspaceSettings(windowSettings.AutoSaveBeforeWorkspaceSwitch, windowSettings.PromptSaveBeforeExit, windowSettings.AutoSnapshotRetentionCount);
        themeMode = windowSettings.Theme;
        ThemeManager.Apply(themeMode);
        RestoreWindowSettings();
        DraftPanelControl.IsExpanded = windowSettings.DraftExpanded;
        this.uiLog.Write("window startup; settings loaded");
        DataContext = viewModel;
        viewModel.Turns.CollectionChanged += OnTurnsChanged;
        Loaded += OnLoaded;
        Closed += OnClosed;
        Closing += OnClosing;
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
            windowSettings = new WindowSettings(Width, Height, double.IsNaN(Left) ? null : Left, double.IsNaN(Top) ? null : Top, LeftPanelColumn.ActualWidth, RightPanelColumn.ActualWidth, themeMode, DraftPanelControl.IsExpanded, viewModel.AutoSaveBeforeWorkspaceSwitch, viewModel.PromptSaveBeforeExit, viewModel.AutoSnapshotRetentionCount);
            WindowSettingsStore.Save(windowSettings);
            uiLog.Write("window settings saved");
        }
        catch (Exception exception)
        {
            uiLog.Write($"window settings save exception; type={exception.GetType().FullName}; message={exception.Message}");
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (isExitConfirmed || !viewModel.PromptSaveBeforeExit || viewModel.SelectedWorkspace is null) return;
        if (exitPromptInProgress)
        {
            e.Cancel = true;
            return;
        }

        exitPromptInProgress = true;
        e.Cancel = true;
        try
        {
            bool hasChanges;
            try
            {
                hasChanges = await viewModel.HasUnsavedSnapshotChangesAsync();
            }
            catch (Exception exception)
            {
                viewModel.SetSnapshotOperationError(UiStrings.ExitSnapshotSaveFailed);
                ShowSnapshotError(exception.Message);
                return;
            }

            if (!hasChanges)
            {
                isExitConfirmed = true;
                Close();
                return;
            }

            var choice = ShowExitProtectionDialog();
            if (choice == ExitProtectionChoice.Cancel) return;
            if (choice == ExitProtectionChoice.ExitWithoutSaving)
            {
                isExitConfirmed = true;
                Close();
                return;
            }

            if (await viewModel.SaveExitSnapshotAsync())
            {
                isExitConfirmed = true;
                Close();
            }
            else
            {
                ShowSnapshotError(viewModel.SnapshotOperationErrorMessage);
            }
        }
        finally
        {
            exitPromptInProgress = false;
        }
    }

    private void OpenSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new SettingsViewModel(viewModel, viewModel.RefreshCodexAsync, themeMode);
            var window = new Window
            {
                Title = (string)(Application.Current.FindResource("UiSettingsWindowTitle") ?? "Codex Bridge 设置"),
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

    private void OpenSnapshotsClick(object sender, RoutedEventArgs e) => viewModel.OpenSnapshotsWindow();

    private async void QuickSaveClick(object sender, RoutedEventArgs e)
    {
        if (await viewModel.QuickSaveCurrentSnapshotAsync()) return;
        ShowSnapshotError(viewModel.SnapshotOperationErrorMessage);
    }

    private ExitProtectionChoice ShowExitProtectionDialog()
    {
        var view = new ExitProtectionView();
        var window = new Window
        {
            Title = (string)Application.Current.FindResource("UiUnsavedSnapshotExitTitle"),
            Width = 460,
            Height = 220,
            Owner = this,
            Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        window.ShowDialog();
        return view.Choice;
    }

    public void ShowSnapshotError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var view = new SnapshotImportErrorView { DataContext = message };
        var window = new Window
        {
            Title = (string)Application.Current.FindResource("UiSnapshotOperationErrorTitle"),
            Width = 460,
            Height = 210,
            Owner = this,
            Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
        };
        window.ShowDialog();
    }

    private void OpenWorkspaceClick(object sender, RoutedEventArgs e)
    {
        var window = new Window
        {
            Title = (string)(Application.Current.FindResource("UiWorkspaceWindowTitle") ?? "Codex Bridge 工作区"), Width = 620, Height = 560, Owner = this,
            Content = new WorkspaceView { DataContext = viewModel },
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.ShowDialog();
    }

    private async void WorkspaceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressWorkspaceComboBoxSelection || viewModel.IsWorkspaceSwitchInProgress || e.AddedItems.Count == 0 || e.AddedItems[^1] is not WorkspaceItem candidate) return;

        var committed = viewModel.SelectedWorkspace;
        if (committed is not null && string.Equals(committed.Path, candidate.Path, StringComparison.OrdinalIgnoreCase)) return;

        WorkspaceComboBox.IsEnabled = false;
        try
        {
            var switchCommitted = await viewModel.TrySwitchWorkspaceAsync(candidate);
            if (!switchCommitted && !string.IsNullOrWhiteSpace(viewModel.SnapshotOperationErrorMessage)) ShowSnapshotError(viewModel.SnapshotOperationErrorMessage);
        }
        finally
        {
            WorkspaceComboBox.IsEnabled = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                suppressWorkspaceComboBoxSelection = true;
                try
                {
                    WorkspaceComboBox.GetBindingExpression(Selector.SelectedItemProperty)?.UpdateTarget();
                }
                finally
                {
                    suppressWorkspaceComboBoxSelection = false;
                }
            }));
        }
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
