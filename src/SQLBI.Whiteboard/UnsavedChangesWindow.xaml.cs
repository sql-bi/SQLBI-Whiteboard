using System.Windows;

namespace SQLBI.Whiteboard;

/// <summary>
/// What to do with a named board that has unsaved changes when the window is closing.
/// </summary>
public enum UnsavedChangesAnswer
{
    /// <summary>Closing was called off; the board stays open.</summary>
    Cancel,

    /// <summary>Write the changes to the file, then close.</summary>
    Save,

    /// <summary>Leave the file alone and restore this board at the next start.</summary>
    Keep,

    /// <summary>Throw the changes away; the next start reopens the file as it is on disk.</summary>
    Discard,
}

/// <summary>
/// Asked on the way out of a board that has a name and has strayed from it. A board with no
/// name is never asked about - it is carried into the session and comes back on its own,
/// which is the whole reason an untitled board does not need to interrupt anybody.
/// </summary>
public partial class UnsavedChangesWindow : Window
{
    public UnsavedChangesWindow(string boardName)
    {
        InitializeComponent();
        HeadingText.Text = $"{boardName} has unsaved changes";
    }

    /// <summary>
    /// Cancel until a button says otherwise, so closing the dialog by the title bar or by
    /// Escape leaves the board where it is.
    /// </summary>
    public UnsavedChangesAnswer Result { get; private set; } = UnsavedChangesAnswer.Cancel;

    private void SaveButton_Click(object sender, RoutedEventArgs e) =>
        Answer(UnsavedChangesAnswer.Save);

    private void KeepButton_Click(object sender, RoutedEventArgs e) =>
        Answer(UnsavedChangesAnswer.Keep);

    private void DiscardButton_Click(object sender, RoutedEventArgs e) =>
        Answer(UnsavedChangesAnswer.Discard);

    // Setting DialogResult is what closes the window. Cancel is not answered here: its
    // button is IsCancel, so WPF closes on it and on Escape, and Result is already Cancel.
    private void Answer(UnsavedChangesAnswer answer)
    {
        Result = answer;
        DialogResult = true;
    }
}
