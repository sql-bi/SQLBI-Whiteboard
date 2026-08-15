using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

internal sealed class StrokePreview : FrameworkElement
{
    public static readonly DependencyProperty PenStyleProperty = DependencyProperty.Register(
        nameof(PenStyle),
        typeof(PenStyle),
        typeof(StrokePreview),
        new FrameworkPropertyMetadata(
            InkPalettes.DefaultPen,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom),
        typeof(double),
        typeof(StrokePreview),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public StrokePreview()
    {
        IsHitTestVisible = false;
    }

    public PenStyle PenStyle
    {
        get => (PenStyle)GetValue(PenStyleProperty);
        set => SetValue(PenStyleProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 2 || ActualHeight <= 2)
        {
            return;
        }

        var zoom = Math.Max(0.25, Zoom);
        var attributes = InkDrawingAttributes.Create(PenStyle, zoom);
        var pad = Math.Max(6, Math.Max(attributes.Width, attributes.Height) / 2 + 2);
        var y = ActualHeight / 2;
        var left = Math.Min(pad, ActualWidth / 4);
        var right = Math.Max(left + 1, ActualWidth - pad);
        var points = new StylusPointCollection
        {
            new StylusPoint(left, y, 0.75f),
            new StylusPoint(right, y, 0.75f),
        };
        new Stroke(points, attributes).Draw(drawingContext);
    }
}
