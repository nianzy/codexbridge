using System.Windows.Input;
using CodexBridge.App.Commands;
using CodexBridge.App.Infrastructure;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexBridge.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly Func<Task> refresh;
    private string themeMode;
    private bool autoSaveBeforeWorkspaceSwitch;
    private bool promptSaveBeforeExit;
    private string autoSnapshotRetentionText;
    private string retentionValidationMessage = string.Empty;
    public SettingsViewModel(MainWindowViewModel source, Func<Task> refresh, string themeMode)
    {
        Source = source;
        this.refresh = refresh;
        this.themeMode = themeMode;
        autoSaveBeforeWorkspaceSwitch = source.AutoSaveBeforeWorkspaceSwitch;
        promptSaveBeforeExit = source.PromptSaveBeforeExit;
        autoSnapshotRetentionText = source.AutoSnapshotRetentionCount.ToString();
        RefreshCommand = new AsyncRelayCommand(() => this.refresh());
    }

    public MainWindowViewModel Source { get; }
    public ICommand RefreshCommand { get; }
    public bool AutoSaveBeforeWorkspaceSwitch
    {
        get => autoSaveBeforeWorkspaceSwitch;
        set
        {
            if (autoSaveBeforeWorkspaceSwitch == value) return;
            autoSaveBeforeWorkspaceSwitch = value;
            Source.AutoSaveBeforeWorkspaceSwitch = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoSaveBeforeWorkspaceSwitch)));
        }
    }
    public bool PromptSaveBeforeExit
    {
        get => promptSaveBeforeExit;
        set
        {
            if (promptSaveBeforeExit == value) return;
            promptSaveBeforeExit = value;
            Source.PromptSaveBeforeExit = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PromptSaveBeforeExit)));
        }
    }
    public string AutoSnapshotRetentionText
    {
        get => autoSnapshotRetentionText;
        set
        {
            if (autoSnapshotRetentionText == value) return;
            autoSnapshotRetentionText = value;
            if (int.TryParse(value, out var parsed) && parsed is >= 3 and <= 50)
            {
                RetentionValidationMessage = string.Empty;
                Source.AutoSnapshotRetentionCount = parsed;
            }
            else
            {
                RetentionValidationMessage = UiStrings.SnapshotRetentionValidation;
            }
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoSnapshotRetentionText)));
        }
    }
    public string RetentionValidationMessage
    {
        get => retentionValidationMessage;
        private set
        {
            if (retentionValidationMessage == value) return;
            retentionValidationMessage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RetentionValidationMessage)));
        }
    }
    public string ThemeMode
    {
        get => themeMode;
        set
        {
            if (themeMode == value) return;
            themeMode = value;
            ThemeManager.Apply(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeMode)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
