using System.Windows;

namespace CodexBridge.App;

public enum ExitProtectionChoice
{
    Cancel,
    SaveAndExit,
    ExitWithoutSaving,
}

public partial class ExitProtectionView : System.Windows.Controls.UserControl
{
    public ExitProtectionView() => InitializeComponent();
    public ExitProtectionChoice Choice { get; private set; }

    private void SaveClick(object sender, RoutedEventArgs e) => Complete(ExitProtectionChoice.SaveAndExit);
    private void ExitClick(object sender, RoutedEventArgs e) => Complete(ExitProtectionChoice.ExitWithoutSaving);
    private void CancelClick(object sender, RoutedEventArgs e) => Complete(ExitProtectionChoice.Cancel);

    private void Complete(ExitProtectionChoice choice)
    {
        Choice = choice;
        if (Window.GetWindow(this) is { } window) window.DialogResult = true;
    }
}
