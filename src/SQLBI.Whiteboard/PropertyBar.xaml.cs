using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

/// <summary>
/// What the selected objects have in common, offered above the selection and
/// applied to all of them at once. A row is shown only when every selected
/// object satisfies its test, so the bar never offers a change that would mean
/// something different to one of the things it would change.
/// </summary>
public partial class PropertyBar : UserControl
{
    private readonly List<PropertyBarRow> _rows = [];

    public PropertyBar()
    {
        InitializeComponent();
        AddRow(HasPrimaryColor, BuildColorRow());
        AddRow(HasThickness, BuildThicknessRow());
        AddRow(HasFill, BuildFillRow());
    }

    /// <summary>
    /// Whether the object has one color that the Color row sets: a stroke's
    /// ink, a shape's outline. A later kind of object joins this list and the
    /// one below it rather than the rows themselves.
    /// </summary>
    private static bool HasPrimaryColor(BoardObject item) =>
        item is InkStrokeObject or ShapeBoardObject;

    private static bool HasThickness(BoardObject item) =>
        item is InkStrokeObject or ShapeBoardObject;

    private static bool HasFill(BoardObject item) => item is ShapeBoardObject;

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

        for (DependencyObject? node = hit as DependencyObject;
             node is not null;
             node = node is Visual visual ? VisualTreeHelper.GetParent(visual) : null)
        {
            if (node is ToggleButton button)
            {
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

    private static bool Shared(IReadOnlyList<BoardObject> selection, uint argb) =>
        selection.All(item => ColorOf(item) == argb);

    private static uint? ColorOf(BoardObject item) => item switch
    {
        InkStrokeObject stroke => stroke.Style.Argb,
        ShapeBoardObject shape => shape.OutlineArgb,
        _ => null,
    };

    private static double? ThicknessOf(BoardObject item) => item switch
    {
        InkStrokeObject stroke => stroke.Style.Thickness,
        ShapeBoardObject shape => shape.Thickness,
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
