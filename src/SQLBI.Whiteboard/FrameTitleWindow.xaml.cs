using System.Windows;
using System.Windows.Input;

namespace SQLBI.Whiteboard;

public partial class FrameTitleWindow : Window
{
    public FrameTitleWindow(string title)
    {
        InitializeComponent();
        TitleBox.Text = title;
    }

    /// <summary>
    /// The title chosen, or null when the dialog was cancelled.
    /// </summary>
    public string? Result { get; private set; }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        TitleBox.Focus();
        TitleBox.SelectAll();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = TitleBox.Text.Trim();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
}
