using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SQLBI.Whiteboard;

public enum SessionCommand
{
    New,
    Open,
    Save,
    SaveAs,
    Close,
    Undo,
    Redo,
    Copy,
    Paste,
    FullScreen,
    CanvasOnly,
    AddLiveView,
    FreezeLiveView,
    DisconnectLiveView,
    ReconnectLiveView,
    Preferences,
    About,
}

public partial class SessionChrome : UserControl
{
    private ToggleButton? _openTab;

    public SessionChrome()
    {
        InitializeComponent();
        SetEditEnabled(canUndo: false, canRedo: false);
        SetLiveViewCommands(selected: false, hasTarget: false, frozen: false);
    }

    public event Action<SessionCommand>? CommandRequested;

    public event Action? ViewOpened;

    public bool IsCommandRowOpen => CommandPopup.IsOpen;

    public void Collapse()
    {
        CommandPopup.IsOpen = false;
        ClearTabChecks();
        _openTab = null;
    }

    public void CollapseIfTransient()
    {
        if (!IsStickyTab(_openTab))
        {
            Collapse();
        }
    }

    public void SetEditEnabled(bool canUndo, bool canRedo)
    {
        if (UndoButton is null)
        {
            return;
        }

        UndoButton.IsEnabled = canUndo;
        RedoButton.IsEnabled = canRedo;
    }

    public void SetLiveViewCommands(bool selected, bool hasTarget, bool frozen)
    {
        if (FreezeLiveViewButton is null)
        {
            return;
        }

        FreezeLiveViewButton.IsEnabled = selected && hasTarget;
        DisconnectLiveViewButton.IsEnabled = selected && hasTarget;
        ReconnectLiveViewButton.IsEnabled = selected;
        var resume = hasTarget && frozen;
        FreezeLiveViewLabel.Text = resume ? "Resume" : "Freeze";
        FreezeLiveViewButton.ToolTip = resume
            ? "Resume the selected LiveView"
            : "Freeze the selected LiveView";
        FreezeLiveViewIcon.Data = resume
            ? (Geometry)FindResource("PlayGeometry")
            : (Geometry)FindResource("PauseGeometry");
    }

    public bool TryHandleAltKey(Key key)
    {
        var tab = key switch
        {
            Key.F => FileTab,
            Key.E => EditTab,
            Key.V => ViewTab,
            Key.H => HelpTab,
            _ => null,
        };
        if (tab is null)
        {
            return false;
        }

        OpenTab(tab);
        return true;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tab)
        {
            return;
        }

        if (ReferenceEquals(_openTab, tab) && CommandPopup.IsOpen)
        {
            Collapse();
            return;
        }

        OpenTab(tab);
    }

    private void OpenTab(ToggleButton tab)
    {
        FileRow.Visibility = Visibility.Collapsed;
        EditRow.Visibility = Visibility.Collapsed;
        ViewRow.Visibility = Visibility.Collapsed;
        HelpRow.Visibility = Visibility.Collapsed;
        if (ReferenceEquals(tab, FileTab))
        {
            FileRow.Visibility = Visibility.Visible;
        }
        else if (ReferenceEquals(tab, EditTab))
        {
            EditRow.Visibility = Visibility.Visible;
        }
        else if (ReferenceEquals(tab, ViewTab))
        {
            ViewRow.Visibility = Visibility.Visible;
        }
        else
        {
            HelpRow.Visibility = Visibility.Visible;
        }

        ClearTabChecks();
        tab.IsChecked = true;
        _openTab = tab;
        CommandPopup.IsOpen = true;
        if (ReferenceEquals(tab, ViewTab))
        {
            ViewOpened?.Invoke();
        }
    }

    private void Command_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } ||
            !Enum.TryParse<SessionCommand>(name, out var command))
        {
            return;
        }

        if (!IsStickyTab(_openTab))
        {
            Collapse();
        }

        CommandRequested?.Invoke(command);
    }

    private bool IsStickyTab(ToggleButton? tab) =>
        ReferenceEquals(tab, EditTab) || ReferenceEquals(tab, HelpTab);

    private void CommandPopup_Closed(object? sender, EventArgs e)
    {
        ClearTabChecks();
        _openTab = null;
    }

    private void ClearTabChecks()
    {
        FileTab.IsChecked = false;
        EditTab.IsChecked = false;
        ViewTab.IsChecked = false;
        HelpTab.IsChecked = false;
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CommandPopup.IsOpen)
        {
            Collapse();
            e.Handled = true;
        }
    }
}
