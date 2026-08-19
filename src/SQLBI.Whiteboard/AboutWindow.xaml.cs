using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace SQLBI.Whiteboard;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ProductTitle.Text = "SQLBI Whiteboard" + AppChannel.WindowTitleSuffix;
        VersionLabel.Text = "Version " + ReadVersion();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void SiteLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
        e.Handled = true;
    }

    private static string ReadVersion()
    {
        var informational = typeof(AboutWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return typeof(AboutWindow).Assembly.GetName().Version?.ToString(3) ?? "0.9.1";
    }
}
