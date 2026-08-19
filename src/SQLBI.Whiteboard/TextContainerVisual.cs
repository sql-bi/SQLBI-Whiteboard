using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Geometry;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Viewport;

namespace SQLBI.Whiteboard;

internal static class TextContainerVisual
{
    public const double DefaultWidth = 600;
    public const double MinimumWidth = 160;
    public const double BodyFontSize = 18;
    public const double TitleFontSize = 13;
    public const double TitleBarHeight = 28;
    public const double ContentPadding = 10;
    public const double BodyBottomAllowance = 6;
    public const double BorderThickness = 1;
    public const double LanguageChipWidth = 132;
    public const double LanguageChipHeight = 22;
    public const double LanguageChipMargin = 6;

    private static readonly Typeface TitleTypeface = new(
        new FontFamily("Segoe UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private static readonly Brush BackgroundBrush = CreateFrozenBrush(0xFFFCFCFC);
    private static readonly Brush TitleBackgroundBrush = CreateFrozenBrush(0xFFF7F7F7);
    private static readonly Brush TextBrush = CreateFrozenBrush(0xFF1F2937);
    private static readonly Pen BorderPen = CreateFrozenPen(0xFFD6D9DE, BorderThickness);
    private static readonly Pen DividerPen = CreateFrozenPen(0xFFE4E6EA, BorderThickness);
    private static readonly ConditionalWeakTable<TextBoardObject, CachedTextVisual> VisualCache = new();

    public static double MeasureDesiredHeight(
        string text,
        double width,
        double visualScale,
        double pixelsPerDip = 1,
        string languageId = TextLanguageIds.Plain)
    {
        ITextLanguageService language = TextLanguageRegistry.Resolve(languageId);
        double scale = NormalizeScale(visualScale);
        double padding = ContentPadding * scale;
        double availableWidth = Math.Max(1, width - (2 * padding));
        FormattedText formatted = CreateBodyText(
            text,
            BodyFontSize * scale,
            pixelsPerDip,
            language);
        formatted.MaxTextWidth = availableWidth;
        return Math.Max(
            64 * scale,
            (TitleBarHeight * scale) +
            (2 * padding) +
            formatted.Height +
            (BodyBottomAllowance * scale));
    }

    public static void Draw(
        DrawingContext drawingContext,
        TextBoardObject textObject,
        Camera2D camera,
        double pixelsPerDip,
        double trailingTitleReserve = 0)
    {
        ITextLanguageService language = TextLanguageRegistry.Resolve(textObject.LanguageId);
        CachedTextVisual cachedVisual = GetCachedVisual(textObject, language);
        Rect destination = ToScreenRectangle(textObject.Bounds, camera);
        double scale = NormalizeScale(textObject.VisualScale) * camera.Zoom;
        double titleHeight = Math.Min(destination.Height, TitleBarHeight * scale);
        double padding = ContentPadding * scale;
        double borderThickness = Math.Max(0.5, BorderThickness * scale);

        drawingContext.DrawRectangle(BackgroundBrush, null, destination);
        drawingContext.DrawRectangle(
            TitleBackgroundBrush,
            null,
            new Rect(destination.Left, destination.Top, destination.Width, titleHeight));

        if (destination.Width > 1 && destination.Height > 1)
        {
            var borderPen = borderThickness == BorderPen.Thickness
                ? BorderPen
                : CreateFrozenPen(0xFFD6D9DE, borderThickness);
            var dividerPen = borderThickness == DividerPen.Thickness
                ? DividerPen
                : CreateFrozenPen(0xFFE4E6EA, borderThickness);
            drawingContext.DrawRectangle(null, borderPen, destination);
            drawingContext.DrawLine(
                dividerPen,
                new Point(destination.Left, destination.Top + titleHeight),
                new Point(destination.Right, destination.Top + titleHeight));
        }

        FormattedText title = new(
            cachedVisual.Title,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            TitleTypeface,
            Math.Max(1, TitleFontSize * scale),
            TextBrush,
            pixelsPerDip)
        {
            MaxTextWidth = Math.Max(
                1,
                destination.Width - (2 * padding) - Math.Max(0, trailingTitleReserve)),
            MaxTextHeight = Math.Max(1, titleHeight),
            Trimming = TextTrimming.CharacterEllipsis,
        };
        drawingContext.DrawText(
            title,
            new Point(destination.Left + padding, destination.Top + Math.Max(0, (titleHeight - title.Height) / 2)));

        var bodyRectangle = new Rect(
            destination.Left + padding,
            destination.Top + titleHeight + padding,
            Math.Max(1, destination.Width - (2 * padding)),
            Math.Max(1, destination.Height - titleHeight - (2 * padding)));
        FormattedText body = CreateBodyText(
            textObject.Text,
            Math.Max(1, BodyFontSize * scale),
            pixelsPerDip,
            language);
        body.MaxTextWidth = bodyRectangle.Width;
        body.Trimming = TextTrimming.None;
        ApplyStyles(body, textObject.Text, cachedVisual.Spans);
        drawingContext.PushClip(new RectangleGeometry(bodyRectangle));
        drawingContext.DrawText(body, bodyRectangle.TopLeft);
        drawingContext.Pop();
    }

    private static FormattedText CreateBodyText(
        string text,
        double fontSize,
        double pixelsPerDip,
        ITextLanguageService language) => new(
            string.IsNullOrEmpty(text) ? " " : text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(language.FontFamilyName),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal),
            fontSize,
            TextBrush,
            pixelsPerDip)
        {
            TextAlignment = TextAlignment.Left,
        };

    private static void ApplyStyles(
        FormattedText formatted,
        string source,
        IReadOnlyList<StyledTextSpan> spans)
    {
        foreach (StyledTextSpan span in spans)
        {
            int start = Math.Clamp(span.Start, 0, source.Length);
            int length = Math.Clamp(span.Length, 0, source.Length - start);
            if (length == 0)
            {
                continue;
            }

            formatted.SetForegroundBrush(span.Style.Foreground, start, length);
            formatted.SetFontWeight(span.Style.FontWeight, start, length);
            formatted.SetFontStyle(span.Style.FontStyle, start, length);
        }
    }

    private static CachedTextVisual GetCachedVisual(
        TextBoardObject textObject,
        ITextLanguageService language) =>
        VisualCache.GetValue(
            textObject,
            item =>
            {
                TextLanguageAnalysis analysis = language.Analyze(item.Text, item.Title);
                return new CachedTextVisual(analysis.Title, analysis.Spans);
            });

    private sealed record CachedTextVisual(
        string Title,
        IReadOnlyList<StyledTextSpan> Spans);

    private static Rect ToScreenRectangle(RectD bounds, Camera2D camera)
    {
        PointD topLeft = camera.WorldToScreen(new PointD(bounds.Left, bounds.Top));
        PointD bottomRight = camera.WorldToScreen(new PointD(bounds.Right, bounds.Bottom));
        return new Rect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }

    private static double NormalizeScale(double scale) =>
        double.IsFinite(scale) && scale > 0 ? scale : 1;

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

    private static Pen CreateFrozenPen(uint argb, double thickness)
    {
        var pen = new Pen(CreateFrozenBrush(argb), thickness);
        pen.Freeze();
        return pen;
    }
}
