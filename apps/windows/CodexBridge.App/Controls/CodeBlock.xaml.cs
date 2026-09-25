using System.Windows;
using System.Windows.Controls;

namespace CodexBridge.App.Controls;

public partial class CodeBlock : UserControl
{
    public CodeBlock() => InitializeComponent();

    public static readonly new DependencyProperty LanguageProperty = DependencyProperty.Register(nameof(Language), typeof(string), typeof(CodeBlock));
    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(nameof(Code), typeof(string), typeof(CodeBlock));
    public new string Language { get => (string)GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }
    public string Code { get => (string)GetValue(CodeProperty); set => SetValue(CodeProperty, value); }

    private async void CopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrEmpty(Code)) return;
            Clipboard.SetText(Code);
            CopyButton.Content = "Copied";
            await Task.Delay(2000);
            CopyButton.Content = "Copy";
        }
        catch
        {
            CopyButton.Content = "Failed";
            await Task.Delay(2000);
            CopyButton.Content = "Copy";
        }
    }
}
