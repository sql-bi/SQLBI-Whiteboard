namespace SQLBI.Whiteboard.Export;

/// <summary>
/// One slide or page: a title, optional notes, and the picture that fills it. The pixel
/// size is what the layout scales by, so the PNG itself is never decoded on the way out.
/// </summary>
public sealed record ExportPage(string Title, string? Notes, byte[] Png, int PixelWidth, int PixelHeight);

/// <summary>16:9 or 4:3.</summary>
public enum SlideAspect
{
    Wide = 0,
    Standard = 1,
}

public sealed record DeckOptions(SlideAspect Aspect = SlideAspect.Wide);
