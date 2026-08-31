using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SQLBI.Whiteboard;

/// <summary>
/// Offered the first time in a session that someone picks a tool from the
/// toolbar with the mouse while Mouse drawing is off. Reaching for the toolbar
/// with a mouse is the one moment the application can be sure the question is
/// worth asking.
/// </summary>
public partial class MouseModeOfferWindow : Window
{
    public MouseModeOfferWindow() => InitializeComponent();

    /// <summary>
    /// Whether the person asked for Mouse drawing to be turned on.
    /// </summary>
    public bool EnableRequested { get; private set; }

    /// <summary>
    /// Whether the offer was declined for good. Unchecked by default: a single
    /// Cancel is an answer for this session, not for every one after it.
    /// </summary>
    public bool DoNotShowAgain => DoNotShowAgainBox.IsChecked == true;

    // Nothing starts focused, so the first Tab reaches the checkbox rather than
    // a button already primed to act. Without this the checkbox takes focus on
    // open, and a stray Space would answer the wrong question.
    private void Window_Loaded(object sender, RoutedEventArgs e) => Keyboard.Focus(this);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        // Enter is always handled here, and never reaches a button that WPF
        // would otherwise treat as the default. It acts only on a button the
        // person has deliberately moved focus to, so the answer is theirs.
        e.Handled = true;
        if (Keyboard.FocusedElement is not Button focused)
        {
            return;
        }

        if (ReferenceEquals(focused, EnableButton))
        {
            Enable();
        }
        else if (ReferenceEquals(focused, CancelButton))
        {
            Close();
        }
    }

    private void EnableButton_Click(object sender, RoutedEventArgs e) => Enable();

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Enable()
    {
        EnableRequested = true;
        Close();
    }
}
