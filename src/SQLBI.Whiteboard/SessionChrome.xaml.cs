using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SQLBI.Whiteboard;

public enum SessionCommand
{
    New,
    Open,
    Save,
    SaveAs,
    Export,
    Close,
    Undo,
    Redo,
    Copy,
    Paste,
    FullScreen,
    CanvasOnly,
    BringToFront,
    SendToBack,
    AddLiveView,
    AddFrame,
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
        SetZOrderEnabled(canBringToFront: false, canSendToBack: false);
    }

    public event Action<SessionCommand>? CommandRequested;

    public event Action? ViewOpened;

    public event Action<string>? UpdateDownloadRequested;

    public event Action<string>? UpdateDismissed;

    public bool IsUpdateNoticeVisible => UpdateNotice.Visibility == Visibility.Visible;

    private string? _offeredUpdateVersion;

    public bool IsCommandRowOpen => CommandHost.Visibility == Visibility.Visible;

    public void Collapse()
    {
        CommandHost.Visibility = Visibility.Collapsed;
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

    public void SetZOrderEnabled(bool canBringToFront, bool canSendToBack)
    {
        if (BringToFrontButton is null)
        {
            return;
        }

        BringToFrontButton.IsEnabled = canBringToFront;
        SendToBackButton.IsEnabled = canSendToBack;
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
        FreezeLiveViewIcon.Fill = resume
            ? (Brush)FindResource("LiveViewResumeBrush")
            : (Brush)FindResource("ToolbarIconBrush");
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

        if (ReferenceEquals(_openTab, tab) && IsCommandRowOpen)
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
        CommandHost.Visibility = Visibility.Visible;
        if (ReferenceEquals(tab, ViewTab))
        {
            ViewOpened?.Invoke();
        }

        Dispatcher.BeginInvoke(FocusFirstCommand, DispatcherPriority.Input);
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

    private void ClearTabChecks()
    {
        FileTab.IsChecked = false;
        EditTab.IsChecked = false;
        ViewTab.IsChecked = false;
        HelpTab.IsChecked = false;
    }

    public bool TryHandleCommandKey(Key key)
    {
        if (!IsCommandRowOpen)
        {
            return false;
        }

        if (key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            MoveCommandFocus(key);
            return true;
        }

        if (TryInvokeAccessKey(key))
        {
            return true;
        }

        return false;
    }

    private void FocusFirstCommand()
    {
        var first = VisibleCommands().FirstOrDefault(button => button.IsEnabled);
        first?.Focus();
    }

    private void MoveCommandFocus(Key key)
    {
        var buttons = VisibleCommands().Where(button => button.IsEnabled).ToArray();
        if (buttons.Length == 0)
        {
            return;
        }

        var current = Array.FindIndex(buttons, button => button.IsKeyboardFocusWithin);
        var next = key switch
        {
            Key.Home => 0,
            Key.End => buttons.Length - 1,
            Key.Left => current <= 0 ? buttons.Length - 1 : current - 1,
            _ => current < 0 || current == buttons.Length - 1 ? 0 : current + 1,
        };
        buttons[next].Focus();
    }

    private bool TryInvokeAccessKey(Key key)
    {
        var letter = key.ToString();
        if (letter.Length != 1)
        {
            return false;
        }

        var command = AccessCommandFor(letter[0]);
        if (command is null)
        {
            return false;
        }

        var button = VisibleCommands().FirstOrDefault(item =>
            item.IsEnabled &&
            item.Tag is string name &&
            name == command.Value.ToString());
        if (button is null)
        {
            return false;
        }

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return true;
    }

    private SessionCommand? AccessCommandFor(char letter) =>
        char.ToUpperInvariant(letter) switch
        {
            'N' when FileRow.Visibility == Visibility.Visible => SessionCommand.New,
            'O' when FileRow.Visibility == Visibility.Visible => SessionCommand.Open,
            'S' when FileRow.Visibility == Visibility.Visible => SessionCommand.Save,
            'A' when FileRow.Visibility == Visibility.Visible => SessionCommand.SaveAs,
            'E' when FileRow.Visibility == Visibility.Visible => SessionCommand.Export,
            'Z' when EditRow.Visibility == Visibility.Visible => SessionCommand.Undo,
            'Y' when EditRow.Visibility == Visibility.Visible => SessionCommand.Redo,
            'C' when EditRow.Visibility == Visibility.Visible => SessionCommand.Copy,
            'V' when EditRow.Visibility == Visibility.Visible => SessionCommand.Paste,
            'F' when ViewRow.Visibility == Visibility.Visible => SessionCommand.FullScreen,
            'C' when ViewRow.Visibility == Visibility.Visible => SessionCommand.CanvasOnly,
            'B' when ViewRow.Visibility == Visibility.Visible => SessionCommand.BringToFront,
            'S' when ViewRow.Visibility == Visibility.Visible => SessionCommand.SendToBack,
            'L' when ViewRow.Visibility == Visibility.Visible => SessionCommand.AddLiveView,
            'A' when ViewRow.Visibility == Visibility.Visible => SessionCommand.AddFrame,
            'Z' when ViewRow.Visibility == Visibility.Visible => SessionCommand.FreezeLiveView,
            'D' when ViewRow.Visibility == Visibility.Visible => SessionCommand.DisconnectLiveView,
            'R' when ViewRow.Visibility == Visibility.Visible => SessionCommand.ReconnectLiveView,
            'P' when HelpRow.Visibility == Visibility.Visible => SessionCommand.Preferences,
            'A' when HelpRow.Visibility == Visibility.Visible => SessionCommand.About,
            _ => null,
        };

    public void ShowUpdateNotice(string version)
    {
        _offeredUpdateVersion = version;
        UpdateNoticeText.Text = "SQLBI Whiteboard " + version + " is available.";
        UpdateNotice.Visibility = Visibility.Visible;
    }

    public void HideUpdateNotice()
    {
        _offeredUpdateVersion = null;
        UpdateNotice.Visibility = Visibility.Collapsed;
    }

    private void UpdateDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_offeredUpdateVersion is { } version)
        {
            UpdateDownloadRequested?.Invoke(version);
        }
    }

    private void UpdateDismissButton_Click(object sender, RoutedEventArgs e)
    {
        var version = _offeredUpdateVersion;
        HideUpdateNotice();
        if (version is not null)
        {
            UpdateDismissed?.Invoke(version);
        }
    }

    private IEnumerable<Button> VisibleCommands()
    {
        var row = FileRow.Visibility == Visibility.Visible ? FileRow
            : EditRow.Visibility == Visibility.Visible ? EditRow
            : ViewRow.Visibility == Visibility.Visible ? ViewRow
            : HelpRow.Visibility == Visibility.Visible ? HelpRow
            : null;
        return row?.Children.OfType<Button>() ?? [];
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsCommandRowOpen)
        {
            Collapse();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            return;
        }

        if (TryHandleCommandKey(e.Key))
        {
            e.Handled = true;
        }
    }
}
