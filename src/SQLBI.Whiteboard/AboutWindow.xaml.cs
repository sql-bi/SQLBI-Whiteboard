using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using SQLBI.Whiteboard.Core.Updates;

namespace SQLBI.Whiteboard;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ProductTitle.Text = "SQLBI Whiteboard" + AppChannel.WindowTitleSuffix;
        var version = AppVersion.Informational;
        VersionLabel.Text = "Version " + version;
        ShowUpdateLink(version);
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

    private void ShowUpdateLink(string current)
    {
        if (StorePackage.IsStoreInstall)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        if (!settings.CheckForUpdates ||
            !UpdateVersion.IsNewer(current, settings.LatestKnownVersion) ||
            string.Equals(
                settings.LastDismissedVersion,
                settings.LatestKnownVersion,
                StringComparison.Ordinal))
        {
            return;
        }

        UpdateAvailable.Visibility = Visibility.Visible;
        UpdateAvailableRun.Text = "Version " + settings.LatestKnownVersion + " is available.";
    }
}
