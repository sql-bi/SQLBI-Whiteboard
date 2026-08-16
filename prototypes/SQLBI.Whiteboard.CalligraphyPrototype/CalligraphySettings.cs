using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace SQLBI.Whiteboard.CalligraphyPrototype;

internal sealed record CalligraphySettings(
    double NominalSize,
    double NibWidthMultiplier,
    double NibHeightMultiplier,
    double NibAngle,
    double PressureExponent,
    double PressureInfluence,
    double MinimumWidth,
    double SpeedInfluence,
    double SpeedReference,
    double SpeedSmoothing,
    bool FitToCurve)
{
    public static CalligraphySettings CurrentApplication { get; } = new(
        NominalSize: 4,
        NibWidthMultiplier: 3,
        NibHeightMultiplier: 0.65,
        NibAngle: 90,
        PressureExponent: 0.65,
        PressureInfluence: 1,
        MinimumWidth: 0.04,
        SpeedInfluence: 1,
        SpeedReference: 1.25,
        SpeedSmoothing: 0.65,
        FitToCurve: false);

    public static CalligraphySettings Stronger { get; } = new(
        NominalSize: 4,
        NibWidthMultiplier: 2.8,
        NibHeightMultiplier: 0.5,
        NibAngle: 35,
        PressureExponent: 1.5,
        PressureInfluence: 1,
        MinimumWidth: 0.02,
        SpeedInfluence: 0.9,
        SpeedReference: 1,
        SpeedSmoothing: 0.6,
        FitToCurve: false);

    public float CalculateEffectivePressure(float rawPressure, double speed)
    {
        double pressure = rawPressure <= 0
            ? 0.25
            : Math.Clamp(rawPressure, 0.001f, 1f);
        double curvedPressure = Math.Pow(pressure, PressureExponent);
        double pressureScale =
            (1 - PressureInfluence) + (PressureInfluence * curvedPressure);
        double normalizedSpeed = Math.Max(0, speed) /
            (Math.Max(0, speed) + Math.Max(0.01, SpeedReference));
        double speedScale = 1 - (SpeedInfluence * normalizedSpeed);
        double effective = MinimumWidth +
            ((1 - MinimumWidth) * pressureScale * speedScale);
        return (float)Math.Clamp(effective, MinimumWidth, 1);
    }

    public DrawingAttributes CreateDrawingAttributes()
    {
        Matrix transform = Matrix.Identity;
        transform.Rotate(NibAngle);
        return new DrawingAttributes
        {
            Color = Colors.Black,
            Width = Math.Max(0.1, NominalSize * NibWidthMultiplier),
            Height = Math.Max(0.1, NominalSize * NibHeightMultiplier),
            FitToCurve = FitToCurve,
            IgnorePressure = false,
            IsHighlighter = false,
            StylusTip = StylusTip.Rectangle,
            StylusTipTransform = transform,
        };
    }
}
