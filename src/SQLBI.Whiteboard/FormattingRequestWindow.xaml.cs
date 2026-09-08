using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

namespace SQLBI.Whiteboard;

/// <summary>
/// The answer F6 gives for a language Whiteboard colors but does not format.
/// It asks for a vote rather than apologizing: the issue's reactions are what
/// decides which language gets a formatter next. Nothing is sent from here -
/// the link opens the issue in the browser when someone activates it, and
/// GitHub handles the sign-in and the reaction.
/// </summary>
public partial class FormattingRequestWindow : Window
{
    private readonly Uri _requestUri;

    public FormattingRequestWindow(string languageName, Uri requestUri)
    {
        InitializeComponent();
        _requestUri = requestUri;
        Title = languageName + " formatting";
        Heading.Text = $"{languageName} formatting is not available yet";
        Body.Text =
            $"Vote for {languageName} formatting and automatic detection on GitHub. " +
            "Add a thumbs-up reaction to the issue's opening post to help us prioritize this language.";
        VoteLink.NavigateUri = requestUri;
        AddressBox.Text = requestUri.AbsoluteUri;
    }

    private void VoteLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo(_requestUri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[FormattingRequest] Opening the browser failed: {exception.Message}");
            BrowserFailedText.Visibility = Visibility.Visible;
            AddressBox.Visibility = Visibility.Visible;
            AddressBox.Focus();
            AddressBox.SelectAll();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
