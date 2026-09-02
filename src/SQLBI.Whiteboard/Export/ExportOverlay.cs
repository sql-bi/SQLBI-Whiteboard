using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Export;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard.Export;

/// <summary>
/// The numbered outlines that show where each area is, on the dialog's preview
/// and on the overview slide. Drawn by the same code so that the two agree.
/// </summary>
internal static class ExportOverlay
{
    private static readonly Brush OutlineBrush = CreateFrozenBrush(0xFF2563EB);
    private static readonly Brush BadgeTextBrush = CreateFrozenBrush(0xFFFFFFFF);
    private static readonly Brush ScaledBadgeBrush = CreateFrozenBrush(0xFFD97706);
    private static readonly Typeface BadgeTypeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.Bold,
        FontStretches.Normal);

    /// <summary>
    /// Draws every area's outline and number. <paramref name="scale"/> sizes the
    /// lines and badges: 1 for a preview around 1000 pixels wide, larger for an
    /// export bitmap, so the badges stay readable at either size.
    /// </summary>
    public static void DrawAreas(
        DrawingContext drawingContext,
        Camera2D camera,
        IReadOnlyList<ExportArea> areas,
        double scale,
        double pixelsPerDip = 1)
    {
        var lineWidth = Math.Max(1, 2 * scale);
        var pen = new Pen(OutlineBrush, lineWidth)
        {
            DashStyle = new DashStyle([4, 3], 0),
            DashCap = PenLineCap.Flat,
        };
        pen.Freeze();
        var inset = 6 * scale;
        var badgeRadius = 14 * scale;

        foreach (var area in areas)
        {
            var rectangle = ToScreen(area.Bounds.Inflate(inset / Math.Max(camera.Zoom, 0.000001)), camera);
            drawingContext.DrawRoundedRectangle(null, pen, rectangle, 4 * scale, 4 * scale);

            // On the corner rather than inside it, so the badge covers as little
            // of the area as a badge can.
            var center = new Point(rectangle.Left, rectangle.Top);
            var badgeBrush = area.IsScaledDown ? ScaledBadgeBrush : OutlineBrush;
            drawingContext.DrawEllipse(badgeBrush, null, center, badgeRadius, badgeRadius);

            var label = new FormattedText(
                area.Number.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                BadgeTypeface,
                Math.Max(1, 15 * scale),
                BadgeTextBrush,
                pixelsPerDip);
            drawingContext.DrawText(
                label,
                new Point(center.X - (label.Width / 2), center.Y - (label.Height / 2)));

            if (area.IsScaledDown)
            {
                var percent = new FormattedText(
                    area.TextScalePercent.ToString(CultureInfo.InvariantCulture) + "%",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    BadgeTypeface,
                    Math.Max(1, 12 * scale),
                    ScaledBadgeBrush,
                    pixelsPerDip);
                drawingContext.DrawText(
                    percent,
                    new Point(center.X + badgeRadius + (4 * scale), center.Y - (percent.Height / 2)));
            }
        }
    }

    private static Rect ToScreen(RectD bounds, Camera2D camera)
    {
        var topLeft = camera.WorldToScreen(new PointD(bounds.Left, bounds.Top));
        var bottomRight = camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        return new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    private static SolidColorBrush CreateFrozenBrush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
        brush.Freeze();
        return brush;
    }
}
