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
/// What the selected objects have in common, offered above the selection and
/// applied to all of them at once. A row is shown only when every selected
/// object satisfies its test, so the bar never offers a change that would mean
/// something different to one of the things it would change.
/// </summary>
public partial class PropertyBar : UserControl
{
    private readonly List<PropertyBarRow> _rows = [];

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
        AddRow(IsLabel, BuildFontRow());
        AddRow(CanTurn, BuildRotateRow());
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

    private static bool IsLabel(BoardObject item) => item is FreeTextBoardObject;

    /// <summary>
    /// What the two quarter turns apply to. A shape and a label are turned the
    /// same way - about their own centre, in steps - so they share the row,
    /// while a shape keeps the font row to itself until it can carry text.
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
    /// A step of the rotation, in degrees: -45 or 45, for every selected label
    /// and shape.
    /// </summary>
    public event Action<double>? RotationStepped;

    /// <summary>
    /// Fills the bar for this selection and says whether anything is left to
    /// show. Nothing in common means no bar at all, rather than an empty one.
    /// </summary>
    public bool Update(IReadOnlyList<BoardObject> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
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

        return shown;
    }

    /// <summary>
    /// A pen tap raised as a click. The board's ink surface owns the stylus, so
    /// a control floating over it cannot rely on the promotion to mouse events
    /// a real mouse gets - the tool palette answers the same problem the same
    /// way.
    /// </summary>
    private void PropertyBar_PreviewStylusDown(object sender, StylusDownEventArgs e)
    {
        if (InputHitTest(e.GetPosition(this)) is not { } hit)
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
            if (node is ButtonBase button)
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
    /// every selected label, and what is shown is what they already share - a
    /// mixed selection shows an empty box rather than the first one's answer.
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
                FreeTextBoardObject[] labels = selection.OfType<FreeTextBoardObject>().ToArray();
                _updatingFontRow = true;
                fonts.SelectedItem = Common(labels, label => label.FontFamily);
                sizes.SelectedItem = Common(labels, label => label.FontSize);
                bold.IsChecked = labels.All(label => label.Bold);
                italic.IsChecked = labels.All(label => label.Italic);
                underline.IsChecked = labels.All(label => label.Underline);
                _updatingFontRow = false;
            },
        };
    }

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
    /// What every selected label says, or nothing when they disagree.
    /// </summary>
    private static object? Common<T>(
        IReadOnlyList<FreeTextBoardObject> labels,
        Func<FreeTextBoardObject, T> property)
        where T : notnull
    {
        if (labels.Count == 0)
        {
            return null;
        }

        T first = property(labels[0]);
        return labels.All(label => property(label).Equals(first)) ? first : null;
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
