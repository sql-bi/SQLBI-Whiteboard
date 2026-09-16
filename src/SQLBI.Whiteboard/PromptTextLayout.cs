using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Model;

namespace SQLBI.Whiteboard;

internal sealed class PromptTextLayout
{
    private static readonly Typeface Typeface = new("Segoe UI");
    internal sealed record Paragraph(FormattedText Body, Point Origin, FormattedText? Marker, double MarkerX);

    public IReadOnlyList<Paragraph> Paragraphs { get; }
    public double Height { get; }

    public PromptTextLayout(string source, double width, double fontSize, double pixelsPerDip, Brush foreground)
    {
        var paragraphs = new List<Paragraph>();
        double y = 0;
        foreach (var line in PromptText.Lines(source))
        {
            var indent = MeasureIndent(line, width, fontSize, pixelsPerDip);
            var body = Format(line.Content, fontSize, pixelsPerDip, foreground);
            body.MaxTextWidth = Math.Max(1, width - indent.Content);
            var marker = line.IsBullet ? Format("•", fontSize, pixelsPerDip, foreground) : null;
            paragraphs.Add(new Paragraph(body, new Point(indent.Content, y), marker, indent.Marker));
            y += body.Height;
        }

        Paragraphs = paragraphs;
        Height = y;
    }

    public void Draw(DrawingContext context, Point origin)
    {
        foreach (var paragraph in Paragraphs)
        {
            if (paragraph.Marker is { } marker)
            {
                context.DrawText(marker, new Point(origin.X + paragraph.MarkerX, origin.Y + paragraph.Origin.Y));
            }

            context.DrawText(paragraph.Body, new Point(origin.X + paragraph.Origin.X, origin.Y + paragraph.Origin.Y));
        }
    }

    public static (double Content, double Marker) MeasureIndent(
        PromptLine line, double width, double fontSize, double pixelsPerDip)
    {
        if (!line.IsBullet)
        {
            return (0, 0);
        }

        var content = Format(line.Text[..line.ContentOffset], fontSize, pixelsPerDip, Brushes.Black).WidthIncludingTrailingWhitespace;
        var marker = line.MarkerOffset == 0 ? 0 :
            Format(line.Text[..line.MarkerOffset], fontSize, pixelsPerDip, Brushes.Black).WidthIncludingTrailingWhitespace;
        // Very deep indentation must not leave a sliver for wrapping.
        return content * 2 < width ? (content, marker) : (Math.Min(fontSize, width / 4), 0);
    }

    private static FormattedText Format(string text, double fontSize, double pixelsPerDip, Brush foreground) =>
        new(string.IsNullOrEmpty(text) ? " " : text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            Typeface, fontSize, foreground, pixelsPerDip);
}
