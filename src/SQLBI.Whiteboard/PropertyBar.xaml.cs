using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

/// <summary>
/// The three switches the font row turns on and off for a label.
/// </summary>
public enum LabelFontStyle
{
    Bold,
    Italic,
    Underline,
}

/// <summary>
/// What the overflow menu offers for the selection as a whole, whatever it is
/// made of. These are the commands the keyboard and the View row already carry,
/// gathered where the selection is.
/// </summary>
public enum SelectionCommand
{
    Delete,
    Copy,
    Duplicate,
    BringToFront,
    BringForward,
    SendBackward,
    SendToBack,
}

/// <summary>
/// What the selected objects have in common, offered above the selection and
/// applied to all of them at once. A row is shown only when every selected
/// object satisfies its test, so the bar never offers a change that would mean
/// something different to one of the things it would change.
/// </summary>
public partial class PropertyBar : UserControl
{
    private readonly List<PropertyBarRow> _rows = [];

    /// <summary>
    /// The overflow's button and the menu it opens, built once and kept: what
    /// changes with the selection is only which of its items can be used.
    /// </summary>
    private readonly Button _overflowButton = new();

    private readonly Popup _overflowPopup = new();

    private readonly Dictionary<SelectionCommand, Button> _overflowItems = [];

    /// <summary>
    /// Set while the font row is brought up to the selection, so that putting a
    /// combo box where the selection already is does not read as a choice.
    /// </summary>
    private bool _updatingFontRow;

    public PropertyBar()
    {
        InitializeComponent();
        AddRow(HasPrimaryColor, BuildColorRow());
        AddRow(HasThickness, BuildThicknessRow());
        AddRow(HasFill, BuildFillRow());
        AddRow(IsConnector, BuildLineKindRow());
        AddRow(HasText, BuildFontRow());
        AddRow(IsShape, BuildShapeTextRow());
        AddRow(CanTurn, BuildRotateRow());
        BuildOverflow();

        // The bar is hidden outright while a gesture is under way, which the
        // menu has no way of noticing on its own.
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible)
            {
                _overflowPopup.IsOpen = false;
            }
        };
    }

    /// <summary>
    /// What the six pen colors mean for each kind of object: a stroke's ink, a
    /// label's text, and - as they arrive - a shape's outline and a connector's
    /// line. Each of these tests is one line, so a new kind of object joins a
    /// row rather than rewriting it.
    /// </summary>
    private static bool HasPrimaryColor(BoardObject item) =>
        item is InkStrokeObject or FreeTextBoardObject or ShapeBoardObject or ConnectorBoardObject;

    private static bool HasThickness(BoardObject item) =>
        item is InkStrokeObject or ShapeBoardObject or ConnectorBoardObject;

    private static bool HasFill(BoardObject item) => item is ShapeBoardObject;

    private static bool IsConnector(BoardObject item) => item is ConnectorBoardObject;

    private static bool IsShape(BoardObject item) => item is ShapeBoardObject;

    /// <summary>
    /// What is written in a font: a label, and a shape, which carries its own
    /// text inside it. A shape's words are colored from a row of its own,
    /// because the color swatches above already mean its outline.
    /// </summary>
    private static bool HasText(BoardObject item) => item is FreeTextBoardObject or ShapeBoardObject;

    /// <summary>
    /// What the two quarter turns apply to. A shape and a label are turned the
    /// same way - about their own centre, in steps - so they share the row.
    /// </summary>
    private static bool CanTurn(BoardObject item) => item is FreeTextBoardObject or ShapeBoardObject;

    /// <summary>
    /// A pen color chosen for everything selected.
    /// </summary>
    public event Action<uint>? ColorChosen;

    /// <summary>
    /// A pen thickness chosen for everything selected.
    /// </summary>
    public event Action<double>? ThicknessChosen;

    /// <summary>
    /// A fill chosen for every selected shape. Null is None.
    /// </summary>
    public event Action<uint?>? FillChosen;

    /// <summary>
    /// A kind chosen for every selected connector.
    /// </summary>
    public event Action<ConnectorKind>? ConnectorKindChosen;

    /// <summary>
    /// A font, a size, a style, or a quarter turn chosen for every selected
    /// label.
    /// </summary>
    public event Action<string>? FontChosen;

    public event Action<double>? FontSizeChosen;

    public event Action<LabelFontStyle, bool>? FontStyleChosen;

    /// <summary>
    /// A color for the text of every selected shape. A shape's outline is what
    /// the color row above means, so its words are colored here instead.
    /// </summary>
    public event Action<uint>? TextColorChosen;

    /// <summary>
    /// The Text button: type inside the one selected shape, which F2 and simply
    /// typing also do.
    /// </summary>
    public event Action? TextEditRequested;

    /// <summary>
    /// A step of the rotation, in degrees: -45 or 45, for every selected label
    /// and shape.
    /// </summary>
    public event Action<double>? RotationStepped;

    /// <summary>
    /// Something the overflow menu asks of the selection as a whole.
    /// </summary>
    public event Action<SelectionCommand>? CommandChosen;

    /// <summary>
    /// Which way the selection can still be moved through the board's depth.
    /// The four commands are two answers: what can go to the front can go one
    /// step forward, and what can go to the back can go one step back.
    /// </summary>
    public void SetZOrderEnabled(bool canMoveForward, bool canMoveBackward)
    {
        _overflowItems[SelectionCommand.BringToFront].IsEnabled = canMoveForward;
        _overflowItems[SelectionCommand.BringForward].IsEnabled = canMoveForward;
        _overflowItems[SelectionCommand.SendBackward].IsEnabled = canMoveBackward;
        _overflowItems[SelectionCommand.SendToBack].IsEnabled = canMoveBackward;
    }

    /// <summary>
    /// Fills the bar for this selection and says whether there is a bar to show.
    /// A selection with nothing in common still has itself: Delete, Copy,
    /// Duplicate, and the four depths apply to a picture, a frame, or a mixture
    /// as much as to a stroke, so the overflow alone is reason enough for the
    /// bar to appear.
    /// </summary>
    public bool Update(IReadOnlyList<BoardObject> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        // Whatever the selection has just become, the menu was opened for what
        // it was.
        _overflowPopup.IsOpen = false;
        var shown = false;
        foreach (var row in _rows)
        {
            var applies = selection.Count > 0 && selection.All(row.AppliesTo);
            row.Content.Visibility = applies ? Visibility.Visible : Visibility.Collapsed;
            if (!applies)
            {
                continue;
            }

            row.Refresh(selection);
            row.Content.Margin = new Thickness(0, shown ? 4 : 0, 0, 0);
            shown = true;
        }

        _overflowButton.Margin = new Thickness(shown ? 4 : 0, 0, 0, 0);
        OverflowHost.Visibility = selection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        return selection.Count > 0;
    }

    /// <summary>
    /// A pen tap raised as a click. The board's ink surface owns the stylus, so
    /// a control floating over it cannot rely on the promotion to mouse events
    /// a real mouse gets - the tool palette answers the same problem the same
    /// way.
    /// </summary>
    private void PropertyBar_PreviewStylusDown(object sender, StylusDownEventArgs e) =>
        PromoteStylusTap(this, e);

    /// <summary>
    /// The same promotion for anything the bar owns. The menu is a popup, and a
    /// popup is a window of its own, so the handler on the bar never sees a tap
    /// in it.
    /// </summary>
    private static void PromoteStylusTap(FrameworkElement root, StylusDownEventArgs e)
    {
        if (root.InputHitTest(e.GetPosition(root)) is not { } hit)
        {
            return;
        }

        // A combo box is asked for first: its own head is a ToggleButton, and
        // promoting that would check the head rather than open the list.
        for (DependencyObject? node = hit as DependencyObject;
             node is not null;
             node = node is Visual visual ? VisualTreeHelper.GetParent(visual) : null)
        {
            if (node is ComboBox combo)
            {
                combo.IsDropDownOpen = true;
                e.Handled = true;
                return;
            }
        }

        for (DependencyObject? node = hit as DependencyObject;
             node is not null;
             node = node is Visual visual ? VisualTreeHelper.GetParent(visual) : null)
        {
            if (node is ButtonBase { IsEnabled: true } button)
            {
                // A promoted click is the whole gesture, since handling it here
                // is what stops the stylus reaching the mouse path at all: a
                // toggle has to be turned over here too, or a row that reads
                // its own state would read the state it had before the tap.
                if (button is ToggleButton toggle)
                {
                    toggle.IsChecked = toggle.IsChecked != true;
                }

                button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                e.Handled = true;
                return;
            }
        }
    }

    private void AddRow(Func<BoardObject, bool> appliesTo, PropertyBarRow row)
    {
        row.AppliesTo = appliesTo;
        _rows.Add(row);
        RowHost.Children.Add(row.Content);
    }

    /// <summary>
    /// The … at the end of the bar and its menu. A popup rather than a
    /// <see cref="ContextMenu"/>: the bar floats over the ink surface, which
    /// owns the stylus, so what opens here has to be something whose taps can be
    /// promoted the way the bar's own buttons are, and a menu item is not a
    /// button.
    /// </summary>
    private void BuildOverflow()
    {
        _overflowButton.Style = (Style)FindResource("PropertyBarButton");
        _overflowButton.ToolTip = "More";
        _overflowButton.Content = new System.Windows.Shapes.Path
        {
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            Fill = (Brush)FindResource("ToolbarIconBrush"),
            Data = (Geometry)FindResource("MoreHorizontalGeometry"),
        };
        _overflowButton.Click += (_, _) => _overflowPopup.IsOpen = !_overflowPopup.IsOpen;
        OverflowHost.Children.Add(_overflowButton);

        var items = new StackPanel();
        foreach ((SelectionCommand command, string name, string shortcut) in OverflowItems)
        {
            Button item = OverflowItem(command, name, shortcut);
            _overflowItems.Add(command, item);
            items.Children.Add(item);
        }

        var frame = new Border
        {
            Padding = new Thickness(4),
            CornerRadius = new CornerRadius(10),
            BorderBrush = (Brush)FindResource("ToolbarBorderBrush"),
            BorderThickness = new Thickness(1),
            Background = (Brush)FindResource("ToolbarBackgroundBrush"),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 1,
                Direction = 270,
                Opacity = 0.16,
                Color = Colors.Black,
            },

            // Room for the shadow, which a transparent popup would otherwise cut.
            Margin = new Thickness(6),
            Child = items,
        };
        Stylus.SetIsPressAndHoldEnabled(frame, false);
        frame.PreviewStylusDown += (_, e) => PromoteStylusTap(frame, e);

        _overflowPopup.Child = frame;
        _overflowPopup.PlacementTarget = _overflowButton;
        _overflowPopup.Placement = PlacementMode.Bottom;
        _overflowPopup.AllowsTransparency = true;
        _overflowPopup.StaysOpen = false;
        _overflowPopup.PopupAnimation = PopupAnimation.Fade;
        OverflowHost.Children.Add(_overflowPopup);
    }

    private Button OverflowItem(SelectionCommand command, string name, string shortcut)
    {
        var content = new DockPanel { LastChildFill = false };
        var hint = new TextBlock
        {
            Text = shortcut,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Margin = new Thickness(24, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("ToolbarHintBrush"),
        };
        DockPanel.SetDock(hint, Dock.Right);
        content.Children.Add(hint);
        content.Children.Add(new TextBlock
        {
            Text = name,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var item = new Button
        {
            Style = (Style)FindResource("PropertyBarMenuItem"),
            Content = content,
        };
        item.Click += (_, _) =>
        {
            _overflowPopup.IsOpen = false;
            CommandChosen?.Invoke(command);
        };
        return item;
    }

    /// <summary>
    /// The menu, in its order: what the selection is, then where it sits. Each
    /// item says the key that does the same thing, where there is one.
    /// </summary>
    private static IReadOnlyList<(SelectionCommand Command, string Name, string Shortcut)> OverflowItems { get; } =
    [
        (SelectionCommand.Delete, "Delete", "Delete"),
        (SelectionCommand.Copy, "Copy", "Ctrl+C"),
        (SelectionCommand.Duplicate, "Duplicate", "Ctrl+D"),
        (SelectionCommand.BringToFront, "Bring to front", string.Empty),
        (SelectionCommand.BringForward, "Bring forward", string.Empty),
        (SelectionCommand.SendBackward, "Send backward", string.Empty),
        (SelectionCommand.SendToBack, "Send to back", string.Empty),
    ];

    private PropertyBarRow BuildColorRow()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var swatch in InkPalettes.Pen)
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("ColorSwatchButton"),
                Background = ToFrozenBrush(swatch.Argb),
                ToolTip = swatch.Name,
                Tag = swatch.Argb,
            };
            button.Click += (_, _) => ColorChosen?.Invoke(swatch.Argb);
            host.Children.Add(button);
        }

        return new PropertyBarRow
        {
            Content = host,
            Refresh = selection =>
            {
                foreach (var button in host.Children.OfType<ToggleButton>())
                {
                    button.IsChecked = button.Tag is uint argb && Shared(selection, argb);
                }
            },
        };
    }

    private PropertyBarRow BuildThicknessRow()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var thickness in InkPalettes.PenThicknesses)
        {
            var preview = new StrokePreview
            {
                Width = 40,
                Height = 26,
                Zoom = 1,
            };
            var button = new ToggleButton
            {
                Style = (Style)FindResource("SizeChipButton"),
                Width = 48,
                Height = 34,
                Content = preview,
                Tag = thickness,
                ToolTip = $"Size {thickness:0}",
            };
            button.Click += (_, _) => ThicknessChosen?.Invoke(thickness);
            host.Children.Add(button);
        }

        return new PropertyBarRow
        {
            Content = host,
            Refresh = selection =>
            {
                // The preview is drawn in the kind the selection already is, so
                // a highlighter set shows highlighter nibs rather than pen ones.
                PenStyle sample = selection.OfType<InkStrokeObject>().FirstOrDefault()?.Style ??
                                  InkPalettes.DefaultPen;
                foreach (var button in host.Children.OfType<ToggleButton>())
                {
                    if (button.Tag is not double thickness)
                    {
                        continue;
                    }

                    button.IsChecked = selection.All(item => ThicknessOf(item) == thickness);
                    if (button.Content is StrokePreview preview)
                    {
                        preview.PenStyle = sample with { Thickness = thickness };
                    }
                }
            },
        };
    }

    /// <summary>
    /// Font, size, bold, italic, and underline. Every change is one step for
    /// everything selected that is written in a font, and what is shown is what
    /// they already share - a mixed selection shows an empty box rather than the
    /// first one's answer.
    /// </summary>
    private PropertyBarRow BuildFontRow()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        var fonts = new ComboBox
        {
            Style = (Style)FindResource("LanguageComboBox"),
            ItemsSource = LabelStyles.Fonts,
            ItemTemplate = FontItemTemplate(),
            Width = 148,
            Height = 26,
            Margin = new Thickness(1, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            ToolTip = "Font",
        };
        fonts.SelectionChanged += (_, _) =>
        {
            if (!_updatingFontRow && fonts.SelectedItem is string font)
            {
                FontChosen?.Invoke(font);
            }
        };
        host.Children.Add(fonts);

        var sizes = new ComboBox
        {
            Style = (Style)FindResource("LanguageComboBox"),
            ItemsSource = LabelStyles.FontSizes,
            ItemTemplate = SizeItemTemplate(),
            Width = 64,
            Height = 26,
            Margin = new Thickness(0, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            ToolTip = "Size",
        };
        sizes.SelectionChanged += (_, _) =>
        {
            if (!_updatingFontRow && sizes.SelectedItem is double size)
            {
                FontSizeChosen?.Invoke(size);
            }
        };
        host.Children.Add(sizes);

        ToggleButton bold = StyleToggle("B", "Bold", FontWeights.Bold, FontStyles.Normal, false);
        ToggleButton italic = StyleToggle("I", "Italic", FontWeights.Normal, FontStyles.Italic, false);
        ToggleButton underline = StyleToggle("U", "Underline", FontWeights.Normal, FontStyles.Normal, true);
        bold.Click += (_, _) => FontStyleChosen?.Invoke(LabelFontStyle.Bold, bold.IsChecked == true);
        italic.Click += (_, _) => FontStyleChosen?.Invoke(LabelFontStyle.Italic, italic.IsChecked == true);
        underline.Click += (_, _) =>
            FontStyleChosen?.Invoke(LabelFontStyle.Underline, underline.IsChecked == true);
        host.Children.Add(bold);
        host.Children.Add(italic);
        host.Children.Add(underline);

        return new PropertyBarRow
        {
            Content = host,
            Refresh = selection =>
            {
                TextStyle[] styles = selection.Select(TextStyleOf).OfType<TextStyle>().ToArray();
                _updatingFontRow = true;
                fonts.SelectedItem = Common(styles, style => style.FontFamily);
                sizes.SelectedItem = Common(styles, style => style.FontSize);
                bold.IsChecked = styles.All(style => style.Bold);
                italic.IsChecked = styles.All(style => style.Italic);
                underline.IsChecked = styles.All(style => style.Underline);
                _updatingFontRow = false;
            },
        };
    }

    /// <summary>
    /// The six pen colors for a shape's own words, and the button that opens
    /// the editor on them. The swatches stand beside the font row rather than
    /// replacing the color row above, which is the shape's outline and has to
    /// keep saying so. The button is for one shape: there is one editor, and it
    /// stands over one shape's text box.
    /// </summary>
    private PropertyBarRow BuildShapeTextRow()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        host.Children.Add(new TextBlock
        {
            Text = "A",
            FontFamily = new FontFamily("Georgia"),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(4, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("ToolbarIconBrush"),
        });

        foreach (var swatch in InkPalettes.Pen)
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("ColorSwatchButton"),
                Background = ToFrozenBrush(swatch.Argb),
                ToolTip = swatch.Name + " text",
                Tag = swatch.Argb,
            };
            button.Click += (_, _) => TextColorChosen?.Invoke(swatch.Argb);
            host.Children.Add(button);
        }

        var edit = new Button
        {
            Style = (Style)FindResource("SizeChipButton"),
            Width = 52,
            Height = 30,
            Margin = new Thickness(6, 0, 0, 0),
            ToolTip = "Type in the shape (F2)",
            Content = new TextBlock
            {
                Text = "Text",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        edit.Click += (_, _) => TextEditRequested?.Invoke();
        host.Children.Add(edit);

        return new PropertyBarRow
        {
            Content = host,
            Refresh = selection =>
            {
                edit.Visibility = selection.Count == 1 ? Visibility.Visible : Visibility.Collapsed;
                foreach (var button in host.Children.OfType<ToggleButton>())
                {
                    button.IsChecked = button.Tag is uint argb &&
                                       selection.All(item =>
                                           item is ShapeBoardObject shape && shape.TextArgb == argb);
                }
            },
        };
    }

    /// <summary>
    /// How an object's text is written, for the row that shows what the
    /// selection has in common. A shape and a label answer the same five
    /// questions.
    /// </summary>
    private readonly record struct TextStyle(
        string FontFamily,
        double FontSize,
        bool Bold,
        bool Italic,
        bool Underline);

    private static TextStyle? TextStyleOf(BoardObject item) => item switch
    {
        FreeTextBoardObject label =>
            new TextStyle(label.FontFamily, label.FontSize, label.Bold, label.Italic, label.Underline),
        ShapeBoardObject shape =>
            new TextStyle(shape.FontFamily, shape.FontSize, shape.Bold, shape.Italic, shape.Underline),
        _ => null,
    };

    /// <summary>
    /// The two quarter turns, for whatever is turned about its own centre: a
    /// row of their own rather than the tail of the font row, since a shape has
    /// an angle without having a font.
    /// </summary>
    private PropertyBarRow BuildRotateRow()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        host.Children.Add(RotateButton("↶", "Turn 45° anticlockwise", -45));
        host.Children.Add(RotateButton("↷", "Turn 45° clockwise", 45));
        return new PropertyBarRow
        {
            Content = host,
            Refresh = static _ => { },
        };
    }

    private ToggleButton StyleToggle(
        string glyph,
        string name,
        FontWeight weight,
        FontStyle style,
        bool underline) =>
        new()
        {
            Style = (Style)FindResource("SizeChipButton"),
            Width = 34,
            Height = 30,
            ToolTip = name,
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Georgia"),
                FontSize = 15,
                FontWeight = weight,
                FontStyle = style,
                TextDecorations = underline ? TextDecorations.Underline : null,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

    private Button RotateButton(string glyph, string name, double degrees)
    {
        var button = new Button
        {
            Style = (Style)FindResource("PropertyBarButton"),
            ToolTip = name,
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.Click += (_, _) => RotationStepped?.Invoke(degrees);
        return button;
    }

    /// <summary>
    /// Each font named in itself, which says more about it than its name does.
    /// The row's own template is needed because the shared combo box style
    /// shows a language's display name.
    /// </summary>
    private static DataTemplate FontItemTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding());
        text.SetBinding(TextBlock.FontFamilyProperty, new Binding());
        var template = new DataTemplate { VisualTree = text };
        template.Seal();
        return template;
    }

    private static DataTemplate SizeItemTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding { StringFormat = "{0:0}" });
        var template = new DataTemplate { VisualTree = text };
        template.Seal();
        return template;
    }

    /// <summary>
    /// What everything selected says, or nothing when they disagree.
    /// </summary>
    private static object? Common<TItem, T>(IReadOnlyList<TItem> items, Func<TItem, T> property)
        where T : notnull
    {
        if (items.Count == 0)
        {
            return null;
        }

        T first = property(items[0]);
        return items.All(item => property(item).Equals(first)) ? first : null;
    }

    /// <summary>
    /// None first, then the six pen colors as tints. A shape's fill is there to
    /// group what is inside it rather than to hide it, so None is the choice a
    /// shape starts with and the one that is always reachable.
    /// </summary>
    private PropertyBarRow BuildFillRow()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (uint? fill in ShapeSettings.Fills)
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("ColorSwatchButton"),
                Background = fill is { } argb ? ToFrozenBrush(argb) : Brushes.Transparent,
                ToolTip = fill is null
                    ? "No fill"
                    : InkPalettes.Pen.First(swatch => ShapeSettings.Tint(swatch.Argb) == fill).Name + " fill",
                Tag = fill,
            };
            button.Click += (_, _) => FillChosen?.Invoke(fill);
            host.Children.Add(button);
        }

        return new PropertyBarRow
        {
            Content = host,
            Refresh = selection =>
            {
                foreach (var button in host.Children.OfType<ToggleButton>())
                {
                    var fill = button.Tag as uint?;
                    button.IsChecked = selection.All(item =>
                        item is ShapeBoardObject shape && shape.FillArgb == fill);
                }
            },
        };
    }

    /// <summary>
    /// Line, arrow, curved arrow: the three a connector can be, offered as the
    /// pictures of themselves the Insert row offers.
    /// </summary>
    private PropertyBarRow BuildLineKindRow()
    {
        var host = new StackPanel { Orientation = Orientation.Horizontal };
        foreach ((ConnectorKind kind, string name) in ConnectorKinds)
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("SizeChipButton"),
                Width = 44,
                Height = 30,
                ToolTip = name,
                Tag = kind,
                Content = new System.Windows.Shapes.Path
                {
                    Width = 20,
                    Height = 20,
                    Stretch = Stretch.Uniform,
                    Fill = (Brush)FindResource("ToolbarIconBrush"),
                    Data = (Geometry)FindResource(ConnectorGeometryKey(kind)),
                },
            };
            button.Click += (_, _) => ConnectorKindChosen?.Invoke(kind);
            host.Children.Add(button);
        }

        return new PropertyBarRow
        {
            Content = host,
            Refresh = selection =>
            {
                foreach (var button in host.Children.OfType<ToggleButton>())
                {
                    button.IsChecked = button.Tag is ConnectorKind kind &&
                                       selection.All(item =>
                                           item is ConnectorBoardObject connector && connector.Kind == kind);
                }
            },
        };
    }

    /// <summary>
    /// The three connectors, in the order the Insert row offers them, with the
    /// names that row uses.
    /// </summary>
    public static IReadOnlyList<(ConnectorKind Kind, string Name)> ConnectorKinds { get; } =
    [
        (ConnectorKind.Line, "Line"),
        (ConnectorKind.Arrow, "Arrow"),
        (ConnectorKind.CurvedArrow, "Curved arrow"),
    ];

    public static string ConnectorGeometryKey(ConnectorKind kind) => kind switch
    {
        ConnectorKind.Line => "LineConnectorGeometry",
        ConnectorKind.CurvedArrow => "CurvedArrowConnectorGeometry",
        _ => "ArrowConnectorGeometry",
    };

    private static bool Shared(IReadOnlyList<BoardObject> selection, uint argb) =>
        selection.All(item => ColorOf(item) == argb);

    private static uint? ColorOf(BoardObject item) => item switch
    {
        InkStrokeObject stroke => stroke.Style.Argb,
        FreeTextBoardObject label => label.Argb,
        ShapeBoardObject shape => shape.OutlineArgb,
        ConnectorBoardObject connector => connector.Argb,
        _ => null,
    };

    private static double? ThicknessOf(BoardObject item) => item switch
    {
        InkStrokeObject stroke => stroke.Style.Thickness,
        ShapeBoardObject shape => shape.Thickness,
        ConnectorBoardObject connector => connector.Thickness,
        _ => null,
    };

    private static SolidColorBrush ToFrozenBrush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
        brush.Freeze();
        return brush;
    }

    private sealed class PropertyBarRow
    {
        public Func<BoardObject, bool> AppliesTo { get; set; } = static _ => false;

        public required Panel Content { get; init; }

        public required Action<IReadOnlyList<BoardObject>> Refresh { get; init; }
    }
}
