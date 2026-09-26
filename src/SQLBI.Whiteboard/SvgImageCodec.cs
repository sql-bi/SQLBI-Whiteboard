using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SharpVectors.Converters;
using SharpVectors.Dom;
using SharpVectors.Dom.Svg;
using SharpVectors.Renderers.Wpf;
using SQLBI.Whiteboard.Core.Import;

namespace SQLBI.Whiteboard;

internal static class SvgImageCodec
{
    public static DrawingImage Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return Draw(SvgMarkup.Rewrite(bytes, WpfFontMetrics.Instance));
    }

    private static DrawingImage Draw(byte[] markup)
    {
        var settings = new WpfDrawingSettings
        {
            // Nothing here needs the SharpVectors runtime types; a plain drawing draws faster.
            IncludeRuntime = false,
            TextAsGeometry = false,

            // Without these the drawing is only as large as the marks in it, so an SVG
            // that centres a label in a 300x60 canvas would arrive cropped to the glyphs
            // and stretched. Together they keep the author's viewBox as the container.
            EnsureViewboxSize = true,
            EnsureViewboxPosition = true,

            // Dropped and pasted SVG is untrusted markup. Left at its default this would
            // fetch whatever an <image href> or an external stylesheet names, which turns
            // opening a board into an outbound request.
            ExternalResourcesAccessMode = ExternalResourcesAccessModes.Ignore,
        };
        settings.Visitors.ImageVisitor = PixelSizedBitmapVisitor.Instance;
        foreach (var folder in OfficeCloudFonts.FoldersFor(SvgMarkup.FontFamilies(markup)))
        {
            settings.AddFontLocation(folder);
        }

        using var reader = new FileSvgReader(settings);
        using var stream = new MemoryStream(markup, writable: false);
        var drawing = reader.Read(stream)
            ?? throw new InvalidDataException("The SVG has nothing to draw.");

        var image = new DrawingImage(drawing);
        if (image.CanFreeze)
        {
            image.Freeze();
        }

        return image;
    }

    /// <summary>
    /// Whether the SVG will look the way its author drew it: it decodes, and every font
    /// it names can be found here, installed in Windows or in Office's cache. Text in a
    /// substitute font is drawn wider or narrower than the positions PowerPoint wrote,
    /// which is what makes the PowerPoint import use a picture for that slide instead.
    /// </summary>
    public static bool CanDrawAsAuthored(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        // Judged on the markup as it will be drawn, after a weight written into a family
        // name has become a weight.
        var markup = SvgMarkup.Rewrite(bytes, WpfFontMetrics.Instance);
        var fontsFound = SvgMarkup.FontFamilyLists(markup).All(list =>
            list.Count == 0 || list.Any(WpfFontMetrics.Instance.IsAvailable));
        if (!fontsFound)
        {
            return false;
        }

        try
        {
            Draw(markup);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>
    /// Fonts as WPF finds them, installed in Windows or in Office's cache, for the
    /// rewrites that need to know which families exist and how wide a run is set.
    /// Typefaces are kept per family, weight, and style: a slide asks for the same few
    /// hundreds of times.
    /// </summary>
    private sealed class WpfFontMetrics : ISvgFontMetrics
    {
        public static WpfFontMetrics Instance { get; } = new();

        private readonly ConcurrentDictionary<(string Family, int Weight, bool Italic), Typeface?> _typefaces = new();

        public bool IsAvailable(string family) =>
            OfficeCloudFonts.Has(family) || new Typeface(family).TryGetGlyphTypeface(out _);

        public double? KernedOverPlain(SvgTextRun run)
        {
            if (run.Families.FirstOrDefault(IsAvailable) is not { } family ||
                TypefaceFor(family, run.Weight, run.Italic) is not { } typeface ||
                !typeface.TryGetGlyphTypeface(out var glyphs) ||
                run.Text.Any(char.IsSurrogate))
            {
                return null;
            }

            // Plain is what the renderer draws: each glyph's advance, end to end.
            double plain = 0;
            foreach (var character in run.Text)
            {
                if (!glyphs.CharacterToGlyphMap.TryGetValue(character, out var glyph))
                {
                    return null;
                }

                plain += glyphs.AdvanceWidths[glyph];
            }

            if (plain <= 0)
            {
                return null;
            }

            var kerned = new FormattedText(
                run.Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                1,
                Brushes.Black,
                1).WidthIncludingTrailingWhitespace;
            return kerned / plain;
        }

        private Typeface? TypefaceFor(string family, int weight, bool italic) =>
            _typefaces.GetOrAdd((family, weight, italic), key =>
            {
                var fontFamily = OfficeCloudFonts.Folder(key.Family) is { } folder
                    ? new FontFamily(new Uri(folder + Path.DirectorySeparatorChar), "./#" + key.Family)
                    : new FontFamily(key.Family);
                return new Typeface(
                    fontFamily,
                    key.Italic ? FontStyles.Italic : FontStyles.Normal,
                    FontWeight.FromOpenTypeWeight(key.Weight),
                    FontStretches.Normal);
            });
    }

    /// <summary>
    /// The fonts Microsoft 365 downloads on demand, such as Aptos, its default since 2023.
    /// Office keeps them in its own cache rather than installing them in Windows, so an
    /// SVG that PowerPoint wrote names a font no other application can find, and the
    /// substitute is wide enough to run each positioned piece of text into the next.
    /// Office names each folder after the family inside it. Only the folders an SVG
    /// names are handed to the renderer, because it reads every file in every location
    /// on each decode. Without Microsoft 365 there are no folders and nothing changes.
    /// </summary>
    private static class OfficeCloudFonts
    {
        private static readonly Lazy<IReadOnlyDictionary<string, string>> Folders = new(FindFolders);

        public static bool Has(string family) => Folders.Value.ContainsKey(family);

        public static string? Folder(string family) => Folders.Value.GetValueOrDefault(family);

        public static IEnumerable<string> FoldersFor(IEnumerable<string> families) =>
            families
                .Select(family => Folders.Value.TryGetValue(family, out var folder) ? folder : null)
                .OfType<string>();

        private static IReadOnlyDictionary<string, string> FindFolders()
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var cache = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft",
                    "FontCache");
                if (!Directory.Exists(cache))
                {
                    return found;
                }

                // The cache is versioned (FontCache\4 today); the newest wins a name.
                foreach (var cloudFonts in Directory.EnumerateDirectories(cache)
                             .OrderByDescending(version => Path.GetFileName(version), StringComparer.Ordinal)
                             .Select(version => Path.Combine(version, "CloudFonts"))
                             .Where(Directory.Exists))
                {
                    foreach (var folder in Directory.EnumerateDirectories(cloudFonts))
                    {
                        found.TryAdd(Path.GetFileName(folder), folder);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A cache that cannot be read is the same as no cache: the text falls back.
            }

            return found;
        }
    }

    /// <summary>
    /// Decodes the bitmaps an SVG embeds so that one bitmap pixel is one user unit.
    /// SharpVectors fits an embedded bitmap into its <c>&lt;image&gt;</c> by the WPF
    /// <c>Width</c> and <c>Height</c> of the decoded picture, which are device-independent
    /// units and so scale with the DPI the file declares: a 72-DPI PNG comes out a third
    /// larger than the pixels the author placed, and a 216-DPI logo less than half the
    /// size. The browsers measure the pixels. Handing the renderer the same pixels at
    /// 96 DPI makes the two agree. Anything that is not a base64 data URI, or that WPF
    /// cannot decode, is left to the renderer's own reader.
    /// </summary>
    private sealed class PixelSizedBitmapVisitor : WpfEmbeddedImageVisitor
    {
        public static PixelSizedBitmapVisitor Instance { get; } = new();

        private const double ScreenDpi = 96;

        public override ImageSource? Visit(SvgImageElement element, WpfDrawingContext context)
        {
            var href = element.Href?.AnimVal;
            if (href is null || !href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var comma = href.IndexOf(',');
            if (comma < 0 || !href[..comma].Contains(";base64", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var bytes = Convert.FromBase64String(href[(comma + 1)..]);
                using var stream = new MemoryStream(bytes, writable: false);
                var decoder = BitmapDecoder.Create(
                    stream,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count == 0)
                {
                    return null;
                }

                BitmapSource frame = decoder.Frames[0];
                return Math.Abs(frame.DpiX - ScreenDpi) < 0.01 && Math.Abs(frame.DpiY - ScreenDpi) < 0.01
                    ? frame
                    : AtScreenDpi(frame);
            }
            catch (Exception exception) when (exception is FormatException or IOException or NotSupportedException or ArgumentException)
            {
                return null;
            }
        }

        private static BitmapSource AtScreenDpi(BitmapSource frame)
        {
            var stride = (frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8;
            var pixels = new byte[stride * frame.PixelHeight];
            frame.CopyPixels(pixels, stride, 0);
            var bitmap = BitmapSource.Create(
                frame.PixelWidth,
                frame.PixelHeight,
                ScreenDpi,
                ScreenDpi,
                frame.Format,
                frame.Palette,
                pixels,
                stride);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
