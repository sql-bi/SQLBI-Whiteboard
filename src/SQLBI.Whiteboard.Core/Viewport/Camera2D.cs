using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.Viewport;

public sealed class Camera2D
{
    public const double MinimumZoom = 0.05;
    public const double MaximumZoom = 16;

    public PointD Center { get; private set; }
    public double Zoom { get; private set; } = 1;
    public double ViewportWidth { get; private set; } = 1;
    public double ViewportHeight { get; private set; } = 1;

    public RectD VisibleWorldBounds
    {
        get
        {
            var topLeft = ScreenToWorld(new PointD(0, 0));
            var bottomRight = ScreenToWorld(new PointD(ViewportWidth, ViewportHeight));
            return new RectD(
                topLeft.X,
                topLeft.Y,
                bottomRight.X - topLeft.X,
                bottomRight.Y - topLeft.Y);
        }
    }

    public void Resize(double width, double height)
    {
        ViewportWidth = Math.Max(1, width);
        ViewportHeight = Math.Max(1, height);
    }

    public PointD WorldToScreen(PointD world) => new(
        ((world.X - Center.X) * Zoom) + (ViewportWidth / 2),
        ((world.Y - Center.Y) * Zoom) + (ViewportHeight / 2));

    public PointD ScreenToWorld(PointD screen) => new(
        ((screen.X - (ViewportWidth / 2)) / Zoom) + Center.X,
        ((screen.Y - (ViewportHeight / 2)) / Zoom) + Center.Y);

    public void PanByScreenDelta(PointD screenDelta)
    {
        Center = new PointD(
            Center.X - (screenDelta.X / Zoom),
            Center.Y - (screenDelta.Y / Zoom));
    }

    public void ZoomAt(PointD screenAnchor, double requestedZoom)
    {
        var worldAnchor = ScreenToWorld(screenAnchor);
        Zoom = Math.Clamp(requestedZoom, MinimumZoom, MaximumZoom);
        var worldAfterZoom = ScreenToWorld(screenAnchor);
        Center += worldAnchor - worldAfterZoom;
    }

    public void Frame(RectD worldBounds, double paddingFraction = 0.04)
    {
        if (paddingFraction is < 0 or >= 0.5)
        {
            throw new ArgumentOutOfRangeException(nameof(paddingFraction));
        }

        var usableWidth = ViewportWidth * (1 - (2 * paddingFraction));
        var usableHeight = ViewportHeight * (1 - (2 * paddingFraction));
        var requestedZoom = Math.Min(
            usableWidth / Math.Max(0.000001, worldBounds.Width),
            usableHeight / Math.Max(0.000001, worldBounds.Height));

        Center = worldBounds.Center;
        Zoom = Math.Clamp(requestedZoom, MinimumZoom, MaximumZoom);
    }

    public void Reset()
    {
        Center = new PointD(0, 0);
        Zoom = 1;
    }
}
