using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace SQLBI.Whiteboard;

/// <summary>
/// Shown once at startup when Windows reports neither a pen tablet nor a
/// touchscreen. The application still opens - everything that does not need ink
/// works with a mouse and the keyboard - so this says what is missing rather
/// than refusing to start.
/// </summary>
public partial class NoDigitizerWindow : Window
{
    public NoDigitizerWindow() => InitializeComponent();

    /// <summary>
    /// Whether the notice was dismissed for good. Also the way out of a false
    /// positive, so it is offered on the notice itself rather than only in
    /// Preferences.
    /// </summary>
    public bool DoNotShowAgain => DoNotShowAgainBox.IsChecked == true;

    /// <summary>
    /// Whether Windows reports anything to draw with. The same tablet list the
    /// Finger drawing setting reads, which is a list of digitizers rather than
    /// an answer about what is plugged in right now: a pen that has never been
    /// brought into range can be absent from it.
    /// </summary>
    public static bool HasDrawingDevice()
    {
        foreach (TabletDevice device in Tablet.TabletDevices)
        {
            if (device.Type is TabletDeviceType.Stylus or TabletDeviceType.Touch)
            {
                return true;
            }
        }

        return false;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void DiscussionLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
        e.Handled = true;
    }

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => Close();
}
