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
    public SettingsViewModel(MainWindowViewModel source, Func<Task> refresh, string themeMode)
    {
        Source = source;
        this.refresh = refresh;
        this.themeMode = themeMode;
        RefreshCommand = new AsyncRelayCommand(() => this.refresh());
    }

    public MainWindowViewModel Source { get; }
    public ICommand RefreshCommand { get; }
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
