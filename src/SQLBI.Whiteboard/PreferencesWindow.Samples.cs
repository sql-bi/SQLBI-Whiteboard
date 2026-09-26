using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;
using Path = System.Windows.Shapes.Path;

namespace SQLBI.Whiteboard;

/// <summary>
/// The pictures behind <see cref="SettingEditorKind.DrawnChoice"/>. Each of
/// these settings is about what the board will look like or what a gesture
/// will take, so each choice is shown as a small still picture of the board
/// instead of a name.
///
/// They are drawn at half the size of the laser and toolbar samples, because
/// the Mode category has five of these rows and all five have to be on the
/// screen together at the dialog's default size. Detail that would have become
/// specks at that size - the Insert palette under the tab strip, the glyphs
/// inside the palette, a fourth thing in a bar - is left out, so that what
/// remains is still legible at 100%.
/// No sample carries text unless the thing itself does.
/// </summary>
public partial class PreferencesWindow
{
    private const double SampleWidth = 40;
    private const double SampleHeight = 26;

    /// <summary>
    /// A toolbar miniature carries more than an empty board does, so the two
    /// pictures that hold one are wider than the rest of the family rather
    /// than squeezing the bar into it.
    /// </summary>
    private const double ToolbarSampleWidth = 52;

    // The chrome behind a tab strip or a palette, a shade lighter than the
    // board's own edge so that a panel sitting on the board still has one.
    private static readonly Brush SampleChromeBrush = Frozen(0xFFF1F2F4);

    // The board's grid, which is 8% black on the surface itself. A miniature of
    // it at that strength is a blank board at 100% scaling, so it is drawn
    // darker than the surface draws it.
    private static readonly Brush SampleGridBrush = Frozen(0x40000000);

    private static readonly Brush SampleSelectedFillBrush = Frozen(0x282563EB);

    /// <summary>
    /// The editor for a drawn choice. The row's id picks the pictures, and the
    /// Insert button's are the toolbar, so they follow Layout the way the
    /// Eraser's do.
    /// </summary>
    private FrameworkElement CreateDrawnChoice(SettingDescriptor setting) =>
        setting.Id == SettingsCatalog.Ids.InsertOnToolbar
            ? CreateFollowingChoice(setting, id => DrawnSample(setting.Id, id), compact: true)
            : CreateSampleChoice(setting, id => DrawnSample(setting.Id, id), compact: true);

    /// <summary>
    /// One choice of one drawn row, or null where the row and the choice do not
    /// go together - which is the same answer the other drawn editors give for
    /// an id they cannot read.
    /// </summary>
    internal FrameworkElement? DrawnSample(string settingId, string choiceId) => settingId switch
    {
        SettingsCatalog.Ids.DesignTools =>
            DrawnBoolean(choiceId) is { } tools ? DesignToolsSample(tools) : null,
        SettingsCatalog.Ids.PropertyBar =>
            DrawnBoolean(choiceId) is { } bar ? PropertyBarSample(bar) : null,
        SettingsCatalog.Ids.ExtendedSelection =>
            DrawnBoolean(choiceId) is { } extended ? ExtendedSelectionSample(extended) : null,
        SettingsCatalog.Ids.DepthAndDuplicate =>
            DrawnBoolean(choiceId) is { } depth ? DepthAndDuplicateSample(depth) : null,
        SettingsCatalog.Ids.Grid =>
            Enum.TryParse<GridStyle>(choiceId, out var grid) ? GridSample(grid) : null,
        SettingsCatalog.Ids.LaserHoldMode =>
            Enum.TryParse<LaserHoldMode>(choiceId, out var hold) ? LaserHoldSample(hold) : null,
        SettingsCatalog.Ids.AreaSelection =>
            Enum.TryParse<AreaSelection>(choiceId, out var area) ? AreaSelectionSample(area) : null,
        SettingsCatalog.Ids.ExtendSelection =>
            Enum.TryParse<ExtendSelection>(choiceId, out var extend) ? ExtendSelectionSample(extend) : null,
        SettingsCatalog.Ids.InsertOnToolbar =>
            DrawnBoolean(choiceId) is { } insert ? InsertOnToolbarSample(insert) : null,
        SettingsCatalog.Ids.InsertPalette =>
            DrawnBoolean(choiceId) is { } palette ? InsertPaletteSample(palette) : null,
        SettingsCatalog.Ids.AfterInsert =>
            Enum.TryParse<AfterInsert>(choiceId, out var after) ? AfterInsertSample(after) : null,
        _ => null,
    };

    private static bool? DrawnBoolean(string choiceId) => choiceId switch
    {
        SettingsCatalog.BooleanChoice.On => true,
        SettingsCatalog.BooleanChoice.Off => false,
        _ => null,
    };

    // The tab strip is where the Insert tab appears, so the group is the strip
    // with and without that tab. The palette it opens is left out, because at
    // this size it was a gray box with specks in it, and the tab is what
    // appears and disappears.
    private static FrameworkElement DesignToolsSample(bool on)
    {
        var scene = SampleScene();
        SampleBlock(scene, 0, 0, scene.Width, 9, SampleChromeBrush, radius: 0);
        SampleBlock(scene, 0, 8.4, scene.Width, 0.8, SampleEdgeBrush, radius: 0);
        for (var index = 0; index < 4; index++)
        {
            SampleBlock(scene, 2 + (index * 7), 2.5, 5.5, 4, SampleGhostBrush, radius: 1.5);
        }

        if (on)
        {
            SampleBlock(scene, 30, 2.5, 5.5, 4, SampleAccentBrush, radius: 1.5);
        }

        return SampleFrame(scene);
    }

    // The bar is drawn where it appears: above whatever is selected, which is
    // why the selection is in both pictures and only the bar is not.
    private static FrameworkElement PropertyBarSample(bool on)
    {
        var scene = SampleScene();
        SampleOutline(scene, 13, 13, 13, 7, SampleInkBrush);
        SampleDashed(scene, 11, 11, 17, 11);
        SampleHandle(scene, 11, 11);
        SampleHandle(scene, 28, 22);
        if (!on)
        {
            return SampleFrame(scene);
        }

        SampleBlock(scene, 6, 2, 26, 7, SampleChromeBrush, radius: 2);
        SampleOutline(scene, 6, 2, 26, 7, SampleEdgeBrush, radius: 2, thickness: 0.8);
        for (var index = 0; index < 3; index++)
        {
            SampleDot(scene, 10 + (index * 4.6), 5.5, 3.2, index == 0 ? SampleAccentBrush : SampleGhostBrush);
        }

        for (var index = 0; index < 3; index++)
        {
            SampleDot(scene, 24 + (index * 2.6), 5.5, 1.4, SampleInkBrush);
        }

        return SampleFrame(scene);
    }

    // The same board twice: an area that takes what it covers, and a lasso with
    // one stroke picked out of it by a tap.
    private static FrameworkElement ExtendedSelectionSample(bool on)
    {
        var scene = SampleScene();
        Point[] first = [new(5, 17), new(8, 11), new(11, 16)];
        Point[] second = [new(14, 18), new(17, 12), new(20, 17)];
        SampleOutline(scene, 24, 9, 11, 8, SampleInkBrush);
        if (!on)
        {
            SampleStroke(scene, SampleInkBrush, 1.2, first);
            SampleStroke(scene, SampleInkBrush, 1.2, second);
            SampleDashed(scene, 2, 6, 33, 14);
            return SampleFrame(scene);
        }

        SampleStroke(scene, SampleInkBrush, 1.2, first);
        SampleStroke(scene, SampleSelectedFillBrush, 4, second);
        SampleStroke(scene, SampleAccentBrush, 1.2, second);
        SampleLasso(scene);
        return SampleFrame(scene);
    }

    // Two shapes that overlap are what depth is about, and the copy behind them
    // is what Duplicate leaves; the chevrons are the two commands themselves.
    private static FrameworkElement DepthAndDuplicateSample(bool on)
    {
        var scene = SampleScene();
        if (on)
        {
            SampleOutline(scene, 2, 4, 13, 8, SampleGhostBrush);
        }

        SampleOutline(scene, 5, 8, 13, 8, SampleInkBrush, SampleBoardBrush);
        SampleEllipse(scene, 12, 11, 12, 9, SampleInkBrush, SampleBoardBrush);
        if (!on)
        {
            return SampleFrame(scene);
        }

        SampleStroke(scene, SampleAccentBrush, 1.3, new Point(29, 9), new Point(32, 6), new Point(35, 9));
        SampleStroke(scene, SampleAccentBrush, 1.3, new Point(29, 16), new Point(32, 19), new Point(35, 16));
        return SampleFrame(scene);
    }

    // The grid itself, at the surface's own step scaled down, so the choice is
    // made against what the board will look like.
    private static FrameworkElement GridSample(GridStyle style)
    {
        var scene = SampleScene();
        const double Step = 6;
        if (style == GridStyle.Lines)
        {
            for (var x = Step; x < scene.Width; x += Step)
            {
                SampleBlock(scene, x, 0, 1, scene.Height, SampleGridBrush, radius: 0);
            }

            for (var y = Step; y < scene.Height; y += Step)
            {
                SampleBlock(scene, 0, y, scene.Width, 1, SampleGridBrush, radius: 0);
            }
        }
        else if (style == GridStyle.Dots)
        {
            for (var x = Step; x < scene.Width; x += Step)
            {
                for (var y = Step; y < scene.Height; y += Step)
                {
                    SampleDot(scene, x, y, 1.8, SampleGridBrush);
                }
            }
        }

        return SampleFrame(scene);
    }

    // One timer or two, drawn as what that does to the trail already on the
    // board when the next stroke starts.
    private static FrameworkElement LaserHoldSample(LaserHoldMode mode)
    {
        var scene = SampleScene();
        var older = mode == LaserHoldMode.Shared ? 0.8 : 0.2;
        SampleStroke(scene, LaserBrush(older), 3, new Point(5, 17), new Point(9, 10), new Point(13, 16));
        SampleStroke(scene, LaserBrush(0.8), 3, new Point(23, 17), new Point(27, 10), new Point(31, 16));
        return SampleFrame(scene);
    }

    // One object inside the area and one across its edge, which is the whole
    // difference between the two choices. A second object on the far edge
    // would repeat the same point, and three objects at this size overlapped
    // too much to read.
    private static FrameworkElement AreaSelectionSample(AreaSelection selection)
    {
        var scene = SampleScene();
        var partly = selection == AreaSelection.PartlyInside;
        SampleOutline(
            scene, 22, 12, 14, 8,
            partly ? SampleAccentBrush : SampleInkBrush,
            partly ? SampleSelectedFillBrush : null);
        SampleOutline(scene, 8, 5, 12, 8, SampleAccentBrush, SampleSelectedFillBrush);
        SampleDashed(scene, 3, 2, 26, 15);
        return SampleFrame(scene);
    }

    // Three shapes in a chain and an area over the first, so each round of the
    // setting is a count of how far along the chain the selection went. A
    // fourth link fitted only by making every shape too small to see which of
    // them the area had taken.
    private static FrameworkElement ExtendSelectionSample(ExtendSelection extend)
    {
        var scene = SampleScene();
        var reached = extend switch
        {
            ExtendSelection.Single => 2,
            ExtendSelection.Recursive => 3,
            _ => 1,
        };

        for (var index = 0; index < 3; index++)
        {
            var selected = index < reached;
            SampleOutline(
                scene,
                3 + (index * 12),
                9,
                10,
                9,
                selected ? SampleAccentBrush : SampleInkBrush,
                selected ? SampleSelectedFillBrush : SampleBoardBrush);
        }

        SampleDashed(scene, 1, 6, 12, 15);
        return SampleFrame(scene);
    }

    // The toolbar as Layout has it, with and without the two controls this adds
    // to it. The space is held either way, so turning it on adds a button
    // rather than moving the bar.
    private FrameworkElement InsertOnToolbarSample(bool on)
    {
        var rows = CompactToolbarRows(_settings.CalligraphyAccess);
        var last = (StackPanel)rows.Children[^1];
        last.Children.Add(new Rectangle
        {
            Width = 5,
            Height = 5,
            RadiusX = 1.5,
            RadiusY = 1.5,
            Margin = new Thickness(3, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = SampleAccentBrush,
            Visibility = on ? Visibility.Visible : Visibility.Hidden,
        });
        last.Children.Add(new Polyline
        {
            Points = [new Point(0, 0), new Point(2.5, 2.5), new Point(5, 0)],
            Stroke = SampleAccentBrush,
            StrokeThickness = 1.2,
            Margin = new Thickness(1.5, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = on ? Visibility.Visible : Visibility.Hidden,
        });
        return CompactBoard(rows);
    }

    // A second panel beside the toolbar, which is all this adds, so the
    // toolbar is the same in both pictures.
    private static FrameworkElement InsertPaletteSample(bool on)
    {
        var scene = SampleScene(ToolbarSampleWidth, SampleHeight);
        SampleBlock(scene, scene.Width - 24, 3, 21, 8, SampleChromeBrush, radius: 4);
        SampleOutline(scene, scene.Width - 24, 3, 21, 8, SampleEdgeBrush, radius: 4, thickness: 0.8);
        for (var index = 0; index < 3; index++)
        {
            SampleDot(
                scene,
                scene.Width - 19 + (index * 5.5),
                7,
                4,
                index == 0 ? SampleAccentBrush : SampleGhostBrush);
        }

        if (on)
        {
            SamplePalette(scene, left: 3, top: 11, width: 22, height: 11);
        }

        return SampleFrame(scene);
    }

    // What the next drag will draw: another of the same thing, or a selection.
    private static FrameworkElement AfterInsertSample(AfterInsert after)
    {
        var scene = SampleScene();
        if (after == AfterInsert.KeepTool)
        {
            for (var index = 0; index < 3; index++)
            {
                SampleOutline(scene, 2 + (index * 12), 3, 10, 7, SampleInkBrush);
            }

            // Clear of the shapes, because drawn over the last one the pen
            // looked like a tail on it rather than the tool still in hand.
            SamplePen(scene, 25, 10.2);
            return SampleFrame(scene);
        }

        SampleOutline(scene, 11, 6, 16, 11, SampleInkBrush, SampleSelectedFillBrush);
        SampleHandle(scene, 11, 6);
        SampleHandle(scene, 27, 17);
        SamplePointer(scene, 17, 10);
        return SampleFrame(scene);
    }

    internal static Canvas SampleScene(double width = SampleWidth, double height = SampleHeight) =>
        new()
        {
            Width = width - 2,
            Height = height - 2,
            ClipToBounds = true,
        };

    internal static Border SampleFrame(Canvas scene) => new()
    {
        Width = scene.Width + 2,
        Height = scene.Height + 2,
        CornerRadius = new CornerRadius(4),
        Background = SampleBoardBrush,
        BorderBrush = SampleEdgeBrush,
        BorderThickness = new Thickness(1),
        Child = scene,
    };

    // The board the toolbar miniature sits on, at the family's height and the
    // width a bar needs.
    private static Border CompactBoard(UIElement content) => new()
    {
        Width = ToolbarSampleWidth,
        Height = SampleHeight,
        CornerRadius = new CornerRadius(4),
        Background = SampleBoardBrush,
        BorderBrush = SampleEdgeBrush,
        BorderThickness = new Thickness(1),
        Child = content,
    };

    // The toolbar each layout produces, at this family's size. The chips show
    // only how many rows there are and which tool is in hand, because a bar
    // this narrow has no room for more.
    private static StackPanel CompactToolbarRows(CalligraphyAccess access)
    {
        var rows = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        switch (access)
        {
            case CalligraphyAccess.DualPalette:
                // Two tools' colors and sizes, both on show at once.
                rows.Children.Add(CompactChipRow(4, chevron: false, nibs: false));
                rows.Children.Add(CompactChipRow(4, chevron: false, nibs: false));
                break;
            case CalligraphyAccess.Chevron:
                // One compact bar, the second tool behind a chevron.
                rows.Children.Add(CompactChipRow(4, chevron: true, nibs: false));
                break;
            default:
                // One bar, the nibs trailing the size chips.
                rows.Children.Add(CompactChipRow(3, chevron: false, nibs: true));
                break;
        }

        return rows;
    }

    private static StackPanel CompactChipRow(int chips, bool chevron, bool nibs)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 1),
        };

        for (var index = 0; index < chips; index++)
        {
            var size = nibs ? 3.5 + index : 5;
            row.Children.Add(new Ellipse
            {
                Width = size,
                Height = size,
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = index == 0 ? SampleAccentBrush : SampleGhostBrush,
            });
        }

        if (chevron)
        {
            row.Children.Add(new Polyline
            {
                Points = [new Point(0, 0), new Point(2.5, 2.5), new Point(5, 0)],
                Stroke = SampleInkBrush,
                StrokeThickness = 1.2,
                Margin = new Thickness(2, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (nibs)
        {
            for (var index = 0; index < 2; index++)
            {
                row.Children.Add(new Rectangle
                {
                    Width = 5,
                    Height = index == 0 ? 5 : 2,
                    RadiusX = 1,
                    RadiusY = 1,
                    Margin = new Thickness(2, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Fill = SampleInkBrush,
                });
            }
        }

        return row;
    }

    private static Brush LaserBrush(double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(230 * opacity),
            LaserSettings.TrailRed,
            LaserSettings.TrailGreen,
            LaserSettings.TrailBlue));
        brush.Freeze();
        return brush;
    }

    internal static void SampleBlock(
        Canvas scene,
        double left,
        double top,
        double width,
        double height,
        Brush fill,
        double radius = 2)
    {
        var block = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Fill = fill,
        };
        Place(scene, block, left, top);
    }

    private static void SampleOutline(
        Canvas scene,
        double left,
        double top,
        double width,
        double height,
        Brush stroke,
        Brush? fill = null,
        double radius = 2,
        double thickness = 1)
    {
        var outline = new Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = fill,
        };
        Place(scene, outline, left, top);
    }

    private static void SampleEllipse(
        Canvas scene,
        double left,
        double top,
        double width,
        double height,
        Brush stroke,
        Brush? fill = null)
    {
        var ellipse = new Ellipse
        {
            Width = width,
            Height = height,
            Stroke = stroke,
            StrokeThickness = 1,
            Fill = fill,
        };
        Place(scene, ellipse, left, top);
    }

    // An area a gesture drew, which is dashed on the board too.
    private static void SampleDashed(Canvas scene, double left, double top, double width, double height)
    {
        var area = new Rectangle
        {
            Width = width,
            Height = height,
            Stroke = SampleAccentBrush,
            StrokeThickness = 1,
            StrokeDashArray = [2.5, 2],
            Fill = SampleSelectedFillBrush,
        };
        Place(scene, area, left, top);
    }

    // The lasso, stretched into the board rather than drawn in its own units,
    // so that the one closed freehand outline serves whatever size the family
    // is drawn at.
    private static void SampleLasso(Canvas scene)
    {
        var lasso = new Path
        {
            Data = Geometry.Parse(
                "M8,26 C4,13 20,6 34,8 C52,10 68,8 70,20 " +
                "C72,33 56,41 38,40 C21,39 11,36 8,26 Z"),
            Stretch = Stretch.Fill,
            Width = scene.Width - 4,
            Height = scene.Height - 4,
            Stroke = SampleAccentBrush,
            StrokeThickness = 1,
            StrokeDashArray = [2.5, 2],
            Fill = SampleSelectedFillBrush,
        };
        Place(scene, lasso, 2, 2);
    }

    private static void SampleDot(Canvas scene, double centerX, double centerY, double diameter, Brush fill)
    {
        var dot = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = fill,
        };
        Place(scene, dot, centerX - (diameter / 2), centerY - (diameter / 2));
    }

    // A selection handle, which is a white square with the accent around it, as
    // it is on the board.
    private static void SampleHandle(Canvas scene, double centerX, double centerY)
    {
        var handle = new Rectangle
        {
            Width = 3.6,
            Height = 3.6,
            RadiusX = 1,
            RadiusY = 1,
            Fill = SampleBoardBrush,
            Stroke = SampleAccentBrush,
            StrokeThickness = 1,
        };
        Place(scene, handle, centerX - 1.8, centerY - 1.8);
    }

    private static void SampleStroke(Canvas scene, Brush stroke, double thickness, params Point[] points)
    {
        var line = new Polyline
        {
            Points = [.. points],
            Stroke = stroke,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        };
        Place(scene, line, 0, 0);
    }

    // The Insert palette: a panel with the buttons it holds. The buttons are
    // plain, because at this size a triangle and an arrow four pixels wide were
    // two smudges, so the picture shows the panel rather than the shapes in it.
    private static void SamplePalette(Canvas scene, double left, double top, double width, double height)
    {
        SampleBlock(scene, left, top, width, height, SampleChromeBrush, radius: 2);
        SampleOutline(scene, left, top, width, height, SampleEdgeBrush, radius: 2, thickness: 0.8);
        const double Glyph = 4;
        const double Gap = 2;
        var rowWidth = (3 * Glyph) + (2 * Gap);
        var startLeft = left + ((width - rowWidth) / 2);
        var firstTop = top + ((height - ((2 * Glyph) + Gap)) / 2);
        for (var row = 0; row < 2; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                SampleBlock(
                    scene,
                    startLeft + (column * (Glyph + Gap)),
                    firstTop + (row * (Glyph + Gap)),
                    Glyph,
                    Glyph,
                    SampleGhostBrush,
                    radius: 1);
            }
        }
    }

    // The pen still in hand, drawn at its own proportions and scaled as one
    // thing, so that the body and the tip keep their places against each other.
    private static void SamplePen(Canvas scene, double left, double top)
    {
        const double Scale = 0.8;
        var pen = new Canvas
        {
            Width = 10,
            Height = 17,
            RenderTransform = new ScaleTransform(Scale, Scale),
        };
        var body = new Path
        {
            Data = Geometry.Parse("M2.4,0 L9,2.4 L5,12.6 L2.6,11.7 Z"),
            Fill = SampleInkBrush,
        };
        var tip = new Path
        {
            Data = Geometry.Parse("M5,12.6 L3.4,16.6 L2.6,11.7 Z"),
            Fill = SampleAccentBrush,
        };
        pen.Children.Add(body);
        pen.Children.Add(tip);
        Place(scene, pen, left, top);
    }

    // The arrow the tool hands back to.
    private static void SamplePointer(Canvas scene, double left, double top)
    {
        var pointer = new Path
        {
            Data = Geometry.Parse("M0,0 L0,11.5 L2.9,8.7 L4.7,12.2 L6.6,11.3 L4.8,7.9 L8.4,7.7 Z"),
            Fill = SampleBoardBrush,
            Stroke = SampleInkBrush,
            StrokeThickness = 1,
            Stretch = Stretch.Uniform,
            Width = 6,
            Height = 9,
        };
        Place(scene, pointer, left, top);
    }

    private static void Place(Canvas scene, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        scene.Children.Add(element);
    }
}
