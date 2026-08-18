using System.Windows;

namespace SQLBI.Whiteboard;

public partial class MissingFilesWindow : Window
{
    public MissingFilesWindow(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        InitializeComponent();
        PathsBox.Text = string.Join(Environment.NewLine, paths);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (PathsBox.Text.Length > 0)
        {
            Clipboard.SetText(PathsBox.Text);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
