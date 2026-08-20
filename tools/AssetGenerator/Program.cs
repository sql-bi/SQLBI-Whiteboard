using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SQLBI.Whiteboard.AssetGenerator;

/// <summary>
/// Renders every derived brand asset from the tile and glyph defined below, which mirror
/// src/SQLBI.Whiteboard/Assets/SQLBI.Whiteboard.svg. Change a color here and in the SVG
/// together, then re-run scripts/build-assets.ps1.
/// </summary>
internal static class Program
{
    // Fluent UI System Icons 'whiteboard_24_filled', drawn on a 24x24 grid.
    // F1 selects the nonzero fill rule, which is what SVG applies by default.
    private const string GlyphPath =
        "F1 m15.99 4-3.07 3.06c-.34.34-.6.74-.78 1.18l-.08.23-.74 2.3a2.25 2.25 0 0 0 2.64 2.88l.15-.04 " +
        "2.33-.7a3.5 3.5 0 0 0 1.29-.7l.18-.18L22 7.95v8.8c0 1.8-1.45 3.25-3.25 3.25H5.25A3.25 3.25 0 0 1 " +
        "2 16.75v-4.2l.08-.03.07-.04 3.76-2.36.1-.05A.75.75 0 0 1 7 11l-.04.1-1.2 2.3-.08.14a2.25 2.25 0 0 0 " +
        "3 2.95l.17-.09 1.76-1 .09-.05a.75.75 0 0 0-.74-1.3l-.1.05-1.75 1-.1.04a.75.75 0 0 1-.97-.96l.04-.09 " +
        "1.2-2.28.09-.17a2.25 2.25 0 0 0-3.12-2.87l-.15.08L2 10.81V7.24a3.25 3.25 0 0 1 3.07-3.24L5.25 4h10.74Z" +
        "m5.19-.46.13.13.12.13c.76.89.72 2.23-.13 3.07l-4.28 4.28c-.26.26-.58.45-.94.56l-2.33.7a1 1 0 0 1-1.24-1.27" +
        "l.74-2.29c.11-.34.3-.65.56-.9l4.29-4.29a2.27 2.27 0 0 1 3.08-.12Z";

    // A sheet with the top-right corner turned back, so a board file is distinguishable
    // from the application at a glance in Explorer.
    private const string PagePath =
        "F1 M 56,16 H 160 L 216,72 V 224 A 16,16 0 0 1 200,240 H 56 A 16,16 0 0 1 40,224 V 32 A 16,16 0 0 1 56,16 Z";

    private const string FoldPath = "F1 M 160,16 L 216,72 H 160 Z";

    private static readonly Color BrandRed = Color.FromRgb(0xF4, 0x27, 0x27);
    private static readonly Color BrandRedDeep = Color.FromRgb(0xB7, 0x1D, 0x1D);
    private static readonly Color DialogGround = Color.FromRgb(0xF7, 0xF7, 0xF8);
    private static readonly Color PageEdge = Color.FromRgb(0xD3, 0xD7, 0xDE);
    private static readonly Color PageFold = Color.FromRgb(0xE8, 0xEA, 0xEE);

    /// <summary>Frame sizes of the application icon, matching the set already shipped.</summary>
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>Favicons only need the sizes browsers actually ask for.</summary>
    private static readonly int[] FaviconSizes = [16, 32, 48];

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: AssetGenerator <repository-root>");
            return 1;
        }

        var root = Path.GetFullPath(args[0]);
        var appAssets = Path.Combine(root, "src", "SQLBI.Whiteboard", "Assets");
        var installerAssets = Path.Combine(root, "installer", "wix", "assets");
        var msixAssets = Path.Combine(root, "installer", "msix", "Assets");
        // Listing artwork is uploaded to Partner Center by hand and must not reach the
        // package: build-msix.ps1 copies installer/msix/Assets wholesale into the MSIX.
        var listingAssets = Path.Combine(root, "installer", "msix", "listing");
        // Generated beside the landing page so site/ is one deployable folder.
        var webAssets = Path.Combine(root, "site");
        Directory.CreateDirectory(installerAssets);
        Directory.CreateDirectory(msixAssets);
        Directory.CreateDirectory(listingAssets);
        Directory.CreateDirectory(webAssets);

        Console.WriteLine("Application");
        Write(Path.Combine(appAssets, "SQLBI.Whiteboard.ico"), BuildIcon(IconSizes, RenderTile));
        Write(Path.Combine(appAssets, "SQLBI.Whiteboard.png"), EncodePng(RenderTile(256)));
        Write(Path.Combine(appAssets, "SQLBI.Whiteboard.Document.ico"), BuildIcon(IconSizes, RenderDocument));

        Console.WriteLine("Installer");
        Write(Path.Combine(installerAssets, "banner.png"), EncodePng(RenderBanner()));
        Write(Path.Combine(installerAssets, "background.png"), EncodePng(RenderBackground()));

        Console.WriteLine("Site");
        Write(Path.Combine(webAssets, "favicon.ico"), BuildIcon(FaviconSizes, RenderTile));
        Write(Path.Combine(webAssets, "favicon-16.png"), EncodePng(RenderTile(16)));
        Write(Path.Combine(webAssets, "favicon-32.png"), EncodePng(RenderTile(32)));
        // iOS rounds the corners itself and composites over black, so this one is full bleed.
        Write(Path.Combine(webAssets, "apple-touch-icon.png"), EncodePng(RenderFullBleed(180)));
        // The social cards are not here. tools/render-cards.ps1 rasterises them from the SVGs
        // beside the page with the same engine the site uses, so its output matches the page
        // preview and this generator must not write over it.

        Console.WriteLine("Store");
        Write(Path.Combine(msixAssets, "StoreLogo.png"), EncodePng(RenderTile(50)));
        Write(Path.Combine(msixAssets, "Square44x44Logo.png"), EncodePng(RenderTile(44)));
        Write(Path.Combine(msixAssets, "Square150x150Logo.png"), EncodePng(RenderTile(150)));
        Write(Path.Combine(msixAssets, "Wide310x150Logo.png"), EncodePng(RenderWideTile(310, 150)));
        Write(Path.Combine(msixAssets, "SplashScreen.png"), EncodePng(RenderWideTile(620, 300)));
        Write(Path.Combine(msixAssets, "DocumentLogo.png"), EncodePng(RenderDocument(256)));

        Console.WriteLine("Store listing");
        Write(Path.Combine(listingAssets, "PosterArt-720x1080.png"), EncodePng(RenderPosterArt()));
        Write(Path.Combine(listingAssets, "BoxArt-1080x1080.png"), EncodePng(RenderBoxArt()));

        return 0;
    }

    private static void Write(string path, byte[] content)
    {
        File.WriteAllBytes(path, content);
        Report(path);
    }

    private static void Report(string path)
    {
        var info = new FileInfo(path);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "  {0,-28} {1,8:N0} bytes",
            info.Name,
            info.Length));
    }

    private static Brush TileBrush(double size) => new LinearGradientBrush(
        BrandRed,
        BrandRedDeep,
        new Point(32 * size / 256, 24 * size / 256),
        new Point(224 * size / 256, 232 * size / 256))
    {
        MappingMode = BrushMappingMode.Absolute,
    };

    /// <summary>Draws the glyph so that it fills a square of the given side length, using the
    /// same 32px inset at 256px that the SVG uses.</summary>
    private static void DrawGlyph(DrawingContext context, double x, double y, double side, Brush brush)
    {
        // Geometry.Parse returns a frozen instance, so the transform goes on the context.
        var geometry = Geometry.Parse(GlyphPath);
        var scale = side / 24.0;
        context.PushTransform(new TranslateTransform(x, y));
        context.PushTransform(new ScaleTransform(scale, scale));
        context.DrawGeometry(brush, null, geometry);
        context.Pop();
        context.Pop();
    }

    private static void DrawTile(DrawingContext context, double x, double y, double size)
    {
        var s = size / 256.0;
        context.DrawRoundedRectangle(
            TileBrush(size),
            null,
            new Rect(x + 8 * s, y + 8 * s, 240 * s, 240 * s),
            52 * s,
            52 * s);

        // The hairline reads as noise once the tile is small enough for it to fall below a pixel.
        if (size >= 48)
        {
            var stroke = new Pen(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)), 2 * s);
            context.DrawRoundedRectangle(
                null,
                stroke,
                new Rect(x + 9 * s, y + 9 * s, 238 * s, 238 * s),
                51 * s,
                51 * s);
        }

        DrawGlyph(context, x + 32 * s, y + 32 * s, 192 * s, Brushes.White);
    }

    private static BitmapSource RenderTile(int size) =>
        Render(size, size, context => DrawTile(context, 0, 0, size));

    /// <summary>The icon shown on .wboard files: a sheet carrying the mark, not the app tile.</summary>
    private static BitmapSource RenderDocument(int size) => Render(size, size, context =>
    {
        var s = size / 256.0;
        context.PushTransform(new ScaleTransform(s, s));

        var page = Geometry.Parse(PagePath);
        // Kept at least a hairline wide, otherwise a white sheet vanishes on a light background.
        var edge = new Pen(new SolidColorBrush(PageEdge), Math.Max(2, 0.8 / s));
        context.DrawGeometry(Brushes.White, edge, page);
        context.DrawGeometry(new SolidColorBrush(PageFold), null, Geometry.Parse(FoldPath));

        context.Pop();

        const double glyph = 116;
        DrawGlyph(
            context,
            (256 - glyph) / 2 * s,
            ((256 - glyph) / 2 + 12) * s,
            glyph * s,
            new SolidColorBrush(BrandRed));
    });

    /// <summary>Edge-to-edge variant with no tile corners, for hosts that apply their own mask.</summary>
    private static BitmapSource RenderFullBleed(int size) => Render(size, size, context =>
    {
        var s = size / 256.0;
        context.DrawRectangle(TileBrush(size), null, new Rect(0, 0, size, size));
        DrawGlyph(context, 32 * s, 32 * s, 192 * s, Brushes.White);
    });

    /// <summary>Composition the Store listing images share: the mark reversed out of the brand
    /// gradient, over the product name. Full bleed, because the Store applies its own corners,
    /// and titled, because these are the main logo on surfaces that do not print the name
    /// beside them.</summary>
    private static void DrawListingArt(DrawingContext context, double width, double height, double glyph, double titleSize)
    {
        context.DrawRectangle(
            new LinearGradientBrush(BrandRed, BrandRedDeep, new Point(0, 0), new Point(width, height))
            {
                MappingMode = BrushMappingMode.Absolute,
            },
            null,
            new Rect(0, 0, width, height));

        var title = Text("SQLBI Whiteboard", Segoe(FontWeights.SemiBold), titleSize, Colors.White);
        var tagline = Text(
            "Pen, touch, and live capture",
            Segoe(FontWeights.Normal),
            titleSize * 0.48,
            Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF));

        // The glyph occupies the middle 18 of its 24-unit box, so only that part is measured
        // into the block; centring on the nominal square would sit the artwork low.
        var glyphInk = glyph * 18.0 / 24.0;
        var titleGap = glyph * 0.2;
        var taglineGap = titleSize * 0.3;
        // Measured rather than positioned by hand, so the block stays centered at either size.
        var block = glyphInk + titleGap + title.Height + taglineGap + tagline.Height;

        var y = (height - block) / 2;
        DrawGlyph(context, (width - glyph) / 2, y - (glyph * 3.0 / 24.0), glyph, Brushes.White);
        y += glyphInk + titleGap;
        context.DrawText(title, new Point((width - title.Width) / 2, y));
        y += title.Height + taglineGap;
        context.DrawText(tagline, new Point((width - tagline.Width) / 2, y));

        if (title.Width > width * 0.88)
        {
            Console.Error.WriteLine(
                $"  warning: listing title reaches {title.Width:F0}px of {width * 0.88:F0}px");
        }
    }

    /// <summary>9:16 poster art, which Windows 10 and 11 use as the main listing logo.</summary>
    private static BitmapSource RenderPosterArt() =>
        Render(720, 1080, context => DrawListingArt(context, 720, 1080, 320, 62));

    /// <summary>1:1 box art, which the Store substitutes into layouts the poster does not fit,
    /// and falls back to as the main logo when no poster art is supplied.</summary>
    private static BitmapSource RenderBoxArt() =>
        Render(1080, 1080, context => DrawListingArt(context, 1080, 1080, 420, 84));

    private static Typeface Segoe(FontWeight weight) =>
        new(new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal);

    private static FormattedText Text(string value, Typeface typeface, double size, Color color) =>
        new(
            value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            new SolidColorBrush(color),
            1.0);

    private static BitmapSource RenderWideTile(int width, int height) => Render(width, height, context =>
    {
        context.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        context.DrawRectangle(new SolidColorBrush(BrandRed), null, new Rect(0, 0, width, Math.Max(4, height * 0.03)));
        var tile = Math.Min(height * 0.72, width * 0.28);
        DrawTile(context, width * 0.08, (height - tile) / 2, tile);
    });

    /// <summary>WiX draws the dialog title over the left of the banner, so the mark sits right.</summary>
    private static BitmapSource RenderBanner() => Render(986, 116, context =>
    {
        context.DrawRectangle(Brushes.White, null, new Rect(0, 0, 986, 116));
        context.DrawRectangle(new SolidColorBrush(BrandRed), null, new Rect(0, 112, 986, 4));
        DrawTile(context, 986 - 36 - 80, (116 - 80) / 2.0, 80);
    });

    /// <summary>WiX overlays text from roughly a quarter of the way across, so the artwork
    /// occupies a band on the left and the rest stays a quiet ground.</summary>
    private static BitmapSource RenderBackground() => Render(986, 624, context =>
    {
        const double band = 264;
        context.DrawRectangle(new SolidColorBrush(DialogGround), null, new Rect(0, 0, 986, 624));
        context.DrawRectangle(
            new LinearGradientBrush(
                BrandRed,
                BrandRedDeep,
                new Point(0, 0),
                new Point(band, 624)) { MappingMode = BrushMappingMode.Absolute },
            null,
            new Rect(0, 0, band, 624));

        const double glyph = 132;
        DrawGlyph(context, (band - glyph) / 2, (624 - glyph) / 2, glyph, Brushes.White);
    });

    private static BitmapSource Render(int width, int height, Action<DrawingContext> draw)
    {
        // Drawn at 4x and resampled, which keeps the glyph's thin strokes from breaking up
        // at the smaller icon frames.
        const int Supersample = 4;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(Supersample, Supersample));
            draw(context);
            context.Pop();
        }

        var large = new RenderTargetBitmap(
            width * Supersample,
            height * Supersample,
            96,
            96,
            PixelFormats.Pbgra32);
        large.Render(visual);

        var scaled = new DrawingVisual();
        using (var context = scaled.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(scaled, BitmapScalingMode.HighQuality);
            context.DrawImage(large, new Rect(0, 0, width, height));
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(scaled);
        target.Freeze();
        return target;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Builds an .ico whose frames are PNG-compressed, matching the icon this replaces.</summary>
    private static byte[] BuildIcon(int[] sizes, Func<int, BitmapSource> render)
    {
        var frames = sizes.Select(size => EncodePng(render(size))).ToArray();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);            // reserved
        writer.Write((ushort)1);            // type: icon
        writer.Write((ushort)frames.Length);

        var offset = 6 + (16 * frames.Length);
        for (var i = 0; i < frames.Length; i++)
        {
            // 256 is encoded as 0 in a directory entry.
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            writer.Write((byte)0);          // palette entries
            writer.Write((byte)0);          // reserved
            writer.Write((ushort)1);        // color planes
            writer.Write((ushort)32);       // bits per pixel
            writer.Write(frames[i].Length);
            writer.Write(offset);
            offset += frames[i].Length;
        }

        foreach (var frame in frames)
        {
            writer.Write(frame);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
