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
/// these settings answers a question about what the board will look like or
/// what a gesture will take, and the answer is a small still picture of the
/// board rather than a name for it: the row is chosen by looking.
///
/// Every sample is drawn at the same size as the laser and toolbar samples, on
/// a scene of plain shapes in the dialog's own palette, so that a row of them
/// reads as one choice and a narrowed dialog shrinks them evenly. No sample
/// carries text unless the thing itself does.
/// </summary>
public partial class PreferencesWindow
{
    private const double SampleWidth = 76;
    private const double SampleHeight = 48;

    // The chrome behind a tab strip or a palette, a shade lighter than the
    // board's own edge so that a panel sitting on the board still has one.
    private static readonly Brush SampleChromeBrush = Frozen(0xFFF1F2F4);

    // The board's grid, which is 8% black on the surface itself. A miniature of
    // it at that strength is a blank board at 100% scaling, so it is drawn at
    // the weight the eye needs rather than the weight the surface uses.
    private static readonly Brush SampleGridBrush = Frozen(0x40000000);

    private static readonly Brush SampleSelectedFillBrush = Frozen(0x282563EB);

    /// <summary>
    /// The editor for a drawn choice. The row's id picks the pictures, and the
    /// Insert button's are the toolbar, so they follow Layout the way the
    /// Eraser's do.
    /// </summary>
    private FrameworkElement CreateDrawnChoice(SettingDescriptor setting) =>
        setting.Id == SettingsCatalog.Ids.InsertOnToolbar
            ? CreateFollowingChoice(setting, id => DrawnSample(setting.Id, id))
            : CreateSampleChoice(setting, id => DrawnSample(setting.Id, id));

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

    // The tab strip is where the Insert tab appears, and the palette is what it
    // opens, so the group is drawn as the two things it puts on the screen.
    private static FrameworkElement DesignToolsSample(bool on)
    {
        var scene = SampleScene();
        SampleBlock(scene, 0, 0, scene.Width, 13, SampleChromeBrush, radius: 0);
        SampleBlock(scene, 0, 12.5, scene.Width, 1, SampleEdgeBrush, radius: 0);
        for (var index = 0; index < 4; index++)
        {
            SampleBlock(scene, 4 + (index * 13), 3.5, 11, 6, SampleGhostBrush);
        }

        if (!on)
        {
            return SampleFrame(scene);
        }

        SampleBlock(scene, 56, 3.5, 11, 6, SampleAccentBrush);
        SamplePalette(scene, left: 16, top: 20, width: 42, height: 20);
        return SampleFrame(scene);
    }

    // The bar is drawn where it appears: above whatever is selected, which is
    // why the selection is in both pictures and only the bar is not.
    private static FrameworkElement PropertyBarSample(bool on)
    {
        var scene = SampleScene();
        SampleOutline(scene, 24, 26, 26, 14, SampleInkBrush);
        SampleDashed(scene, 20, 22, 34, 22);
        SampleHandle(scene, 20, 22);
        SampleHandle(scene, 54, 44);
        if (!on)
        {
            return SampleFrame(scene);
        }

        SampleBlock(scene, 15, 4, 44, 13, SampleChromeBrush, radius: 3);
        SampleOutline(scene, 15, 4, 44, 13, SampleEdgeBrush, radius: 3, thickness: 1);
        for (var index = 0; index < 3; index++)
        {
            SampleDot(scene, 22 + (index * 8), 10.5, 5.5, index == 0 ? SampleAccentBrush : SampleGhostBrush);
        }

        for (var index = 0; index < 3; index++)
        {
            SampleDot(scene, 45 + (index * 4), 10.5, 2.2, SampleInkBrush);
        }

        return SampleFrame(scene);
    }

    // The same board twice: an area that takes what it covers, and a lasso with
    // one stroke picked out of it by a tap.
    private static FrameworkElement ExtendedSelectionSample(bool on)
    {
        var scene = SampleScene();
        Point[] first = [new(9, 33), new(15, 22), new(21, 32)];
        Point[] second = [new(27, 34), new(32, 24), new(38, 33)];
        SampleOutline(scene, 44, 18, 22, 16, SampleInkBrush);
        if (!on)
        {
            SampleStroke(scene, SampleInkBrush, 1.8, first);
            SampleStroke(scene, SampleInkBrush, 1.8, second);
            SampleDashed(scene, 5, 12, 62, 28);
            return SampleFrame(scene);
        }

        SampleStroke(scene, SampleInkBrush, 1.8, first);
        SampleStroke(scene, SampleSelectedFillBrush, 6, second);
        SampleStroke(scene, SampleAccentBrush, 1.8, second);
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
            SampleOutline(scene, 9, 11, 28, 18, SampleGhostBrush);
        }

        SampleOutline(scene, 14, 16, 28, 18, SampleInkBrush, SampleBoardBrush);
        SampleEllipse(scene, 30, 22, 24, 17, SampleInkBrush, SampleBoardBrush);
        if (!on)
        {
            return SampleFrame(scene);
        }

        SampleStroke(scene, SampleAccentBrush, 1.8, new Point(60, 16), new Point(64, 11.5), new Point(68, 16));
        SampleStroke(scene, SampleAccentBrush, 1.8, new Point(60, 26), new Point(64, 30.5), new Point(68, 26));
        return SampleFrame(scene);
    }

    // The grid itself, at the surface's own step scaled down, so the choice is
    // made against what the board will look like.
    private static FrameworkElement GridSample(GridStyle style)
    {
        var scene = SampleScene();
        const double Step = 11;
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
                    SampleDot(scene, x, y, 2.4, SampleGridBrush);
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
        SampleStroke(scene, LaserBrush(older), 4.5, new Point(9, 32), new Point(17, 21), new Point(25, 30));
        SampleStroke(scene, LaserBrush(0.8), 4.5, new Point(42, 32), new Point(50, 21), new Point(58, 30));
        return SampleFrame(scene);
    }

    // One object inside the area and two across its edge, which is the whole of
    // the difference between the two answers.
    private static FrameworkElement AreaSelectionSample(AreaSelection selection)
    {
        var scene = SampleScene();
        var partly = selection == AreaSelection.PartlyInside;
        SampleOutline(
            scene, 2, 26, 16, 11,
            partly ? SampleAccentBrush : SampleInkBrush,
            partly ? SampleSelectedFillBrush : null);
        SampleOutline(
            scene, 48, 11, 22, 11,
            partly ? SampleAccentBrush : SampleInkBrush,
            partly ? SampleSelectedFillBrush : null);
        SampleOutline(scene, 21, 15, 18, 12, SampleAccentBrush, SampleSelectedFillBrush);
        SampleDashed(scene, 10, 8, 46, 30);
        return SampleFrame(scene);
    }

    // Four shapes in a chain and an area over the first, so each round of the
    // setting is a count of how far along the chain the selection went.
    private static FrameworkElement ExtendSelectionSample(ExtendSelection extend)
    {
        var scene = SampleScene();
        var reached = extend switch
        {
            ExtendSelection.Single => 2,
            ExtendSelection.Recursive => 4,
            _ => 1,
        };

        for (var index = 0; index < 4; index++)
        {
            var selected = index < reached;
            SampleOutline(
                scene,
                6 + (index * 14),
                20,
                14,
                13,
                selected ? SampleAccentBrush : SampleInkBrush,
                selected ? SampleSelectedFillBrush : SampleBoardBrush);
        }

        SampleDashed(scene, 3, 16, 16, 21);
        return SampleFrame(scene);
    }

    // The toolbar as Layout has it, with and without the two controls this adds
    // to it. The space is held either way, so turning it on adds a button
    // rather than moving the bar.
    private FrameworkElement InsertOnToolbarSample(bool on)
    {
        var rows = SampleToolbarRows(_settings.CalligraphyAccess);
        var last = (StackPanel)rows.Children[^1];
        last.Children.Add(new Rectangle
        {
            Width = 8,
            Height = 8,
            RadiusX = 2,
            RadiusY = 2,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = SampleAccentBrush,
            Visibility = on ? Visibility.Visible : Visibility.Hidden,
        });
        last.Children.Add(new Polyline
        {
            Points = [new Point(0, 0), new Point(3.5, 3.5), new Point(7, 0)],
            Stroke = SampleAccentBrush,
            StrokeThickness = 1.6,
            Margin = new Thickness(2, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = on ? Visibility.Visible : Visibility.Hidden,
        });
        return SampleBoard(rows, width: 92);
    }

    // A second panel beside the toolbar, which is the whole of what this adds:
    // the toolbar is the same in both pictures.
    private static FrameworkElement InsertPaletteSample(bool on)
    {
        var scene = SampleScene(92, 46);
        SampleBlock(scene, scene.Width - 34, 5, 30, 12, SampleChromeBrush, radius: 6);
        SampleOutline(scene, scene.Width - 34, 5, 30, 12, SampleEdgeBrush, radius: 6, thickness: 1);
        for (var index = 0; index < 3; index++)
        {
            SampleDot(
                scene,
                scene.Width - 27 + (index * 8),
                11,
                6,
                index == 0 ? SampleAccentBrush : SampleGhostBrush);
        }

        if (on)
        {
            SamplePalette(scene, left: 6, top: 22, width: 44, height: 20);
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
                SampleOutline(scene, 5 + (index * 21), 9, 18, 13, SampleInkBrush);
            }

            // Clear of the shapes: drawn over the last one the pen read as a
            // tail on it rather than as the tool still in hand.
            SamplePen(scene, 46, 24);
            return SampleFrame(scene);
        }

        SampleOutline(scene, 22, 12, 28, 18, SampleInkBrush, SampleSelectedFillBrush);
        SampleHandle(scene, 22, 12);
        SampleHandle(scene, 50, 30);
        SamplePointer(scene, 33, 18);
        return SampleFrame(scene);
    }

    private static Canvas SampleScene(double width = SampleWidth, double height = SampleHeight) =>
        new()
        {
            Width = width - 2,
            Height = height - 2,
            ClipToBounds = true,
        };

    private static Border SampleFrame(Canvas scene) => new()
    {
        Width = scene.Width + 2,
        Height = scene.Height + 2,
        CornerRadius = new CornerRadius(6),
        Background = SampleBoardBrush,
        BorderBrush = SampleEdgeBrush,
        BorderThickness = new Thickness(1),
        Child = scene,
    };

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

    private static void SampleBlock(
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
        double radius = 3,
        double thickness = 1.4)
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
            StrokeThickness = 1.4,
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
            StrokeThickness = 1.2,
            StrokeDashArray = [3, 2.5],
            Fill = SampleSelectedFillBrush,
        };
        Place(scene, area, left, top);
    }

    private static void SampleLasso(Canvas scene)
    {
        var lasso = new Path
        {
            Data = Geometry.Parse(
                "M8,26 C4,13 20,6 34,8 C52,10 68,8 70,20 " +
                "C72,33 56,41 38,40 C21,39 11,36 8,26 Z"),
            Stroke = SampleAccentBrush,
            StrokeThickness = 1.2,
            StrokeDashArray = [3, 2.5],
            Fill = SampleSelectedFillBrush,
        };
        Place(scene, lasso, 0, 0);
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
            Width = 5,
            Height = 5,
            RadiusX = 1,
            RadiusY = 1,
            Fill = SampleBoardBrush,
            Stroke = SampleAccentBrush,
            StrokeThickness = 1.2,
        };
        Place(scene, handle, centerX - 2.5, centerY - 2.5);
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

    // The Insert palette: two rows of the things it holds, drawn as the shapes
    // themselves rather than as the glyphs, which lose their outline this small.
    private static void SamplePalette(Canvas scene, double left, double top, double width, double height)
    {
        SampleBlock(scene, left, top, width, height, SampleChromeBrush, radius: 3);
        SampleOutline(scene, left, top, width, height, SampleEdgeBrush, radius: 3, thickness: 1);
        const double Glyph = 7;
        const double Gap = 4;
        var rowWidth = (3 * Glyph) + (2 * Gap);
        var startLeft = left + ((width - rowWidth) / 2);
        var firstTop = top + ((height - ((2 * Glyph) + Gap)) / 2);
        FrameworkElement[] first =
        [
            new Rectangle { RadiusX = 1.5, RadiusY = 1.5, Fill = SampleGhostBrush },
            new Ellipse { Fill = SampleGhostBrush },
            new Path { Data = Geometry.Parse("M5,0 L10,10 L0,10 Z"), Fill = SampleGhostBrush, Stretch = Stretch.Uniform },
        ];
        FrameworkElement[] second =
        [
            new Path { Data = Geometry.Parse("M5,0 L10,5 L5,10 L0,5 Z"), Fill = SampleGhostBrush, Stretch = Stretch.Uniform },
            new Path { Data = Geometry.Parse("M0,4 L7,4 L7,2 L10,5 L7,8 L7,6 L0,6 Z"), Fill = SampleGhostBrush, Stretch = Stretch.Uniform },
            new Path { Data = Geometry.Parse("M0,0 L10,0 L10,2.6 L6.3,2.6 L6.3,10 L3.7,10 L3.7,2.6 L0,2.6 Z"), Fill = SampleGhostBrush, Stretch = Stretch.Uniform },
        ];

        PlaceRow(scene, startLeft, firstTop, Glyph, Gap, first);
        PlaceRow(scene, startLeft, firstTop + Glyph + Gap, Glyph, Gap, second);
    }

    // The pen still in hand, drawn at its final size rather than stretched, so
    // that the body and the tip keep their places against each other.
    private static void SamplePen(Canvas scene, double left, double top)
    {
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
        Place(scene, body, left, top);

        Place(scene, tip, left, top);
    }

    // The arrow the tool hands back to.
    private static void SamplePointer(Canvas scene, double left, double top)
    {
        var pointer = new Path
        {
            Data = Geometry.Parse("M0,0 L0,11.5 L2.9,8.7 L4.7,12.2 L6.6,11.3 L4.8,7.9 L8.4,7.7 Z"),
            Fill = SampleBoardBrush,
            Stroke = SampleInkBrush,
            StrokeThickness = 1.1,
            Stretch = Stretch.Uniform,
            Width = 9,
            Height = 13,
        };
        Place(scene, pointer, left, top);
    }

    private static void PlaceRow(
        Canvas scene,
        double left,
        double top,
        double size,
        double gap,
        IReadOnlyList<FrameworkElement> items)
    {
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            item.Width = size;
            item.Height = size;
            Place(scene, item, left + (index * (size + gap)), top);
        }
    }

    private static void Place(Canvas scene, UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        scene.Children.Add(element);
    }
}
