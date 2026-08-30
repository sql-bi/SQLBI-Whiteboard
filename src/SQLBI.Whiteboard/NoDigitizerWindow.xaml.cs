using System.Windows;
using System.Windows.Input;

namespace SQLBI.Whiteboard;

/// <summary>
/// Shown once at startup when Windows reports neither a pen tablet nor a
/// touchscreen. The application still opens, and with Mouse drawing on it can
/// be drawn on, so this says which mode the session is in and what a pen would
/// add rather than apologizing for the machine.
/// </summary>
public partial class NoDigitizerWindow : Window
{
    public NoDigitizerWindow(bool mouseDrawingEnabled)
    {
        InitializeComponent();
        ModeText.Text = mouseDrawingEnabled
            ? "Mouse drawing is on, so the left button uses the tool selected on the toolbar. Hold Ctrl and drag to move or resize a container, and Ctrl with a double-click to fit one to the canvas."
            : "Mouse drawing is turned off, so a mouse pans, zooms, and moves containers, but does not draw. Turn it on under Help > Preferences > Input.";
        LimitsText.Visibility = mouseDrawingEnabled ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Whether the notice was dismissed for good. Also the way out of a false
    /// positive, so it is offered on the notice itself rather than only in
    /// Preferences.
    /// </summary>
    public bool DoNotShowAgain => DoNotShowAgainBox.IsChecked == true;

    /// <summary>
    /// Whether Windows reports anything to draw with by hand. The same tablet
    /// list the Finger drawing setting reads, which is a list of digitizers
    /// rather than an answer about what is plugged in right now: a pen that has
    /// never been brought into range can be absent from it.
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

    private void ContinueButton_Click(object sender, RoutedEventArgs e) => Close();
}
