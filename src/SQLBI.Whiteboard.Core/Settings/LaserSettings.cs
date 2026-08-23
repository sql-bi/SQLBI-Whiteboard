namespace SQLBI.Whiteboard.Core.Settings;

public enum LaserHoldMode
{
    Shared = 0,
    PerStroke = 1,
}

/// <summary>
/// How much a light touch is thinned out. The pen reports very little pressure
/// on a quick tap, which at <see cref="LaserTrailWeight.Light"/> draws a line
/// thin enough to miss on a projector. The heavier settings raise the floor
/// without changing what a firm stroke looks like.
/// </summary>
public enum LaserTrailWeight
{
    Light = 0,
    Medium = 1,
    Bold = 2,
}

public sealed class LaserSettings
{
    public const double DefaultHoldSeconds = 2;
    public const double DefaultFadeSeconds = 0.85;
    public const double MinimumHoldSeconds = 0;
    public const double MaximumHoldSeconds = 30;
    public const double MinimumFadeSeconds = 0.05;
    public const double MaximumFadeSeconds = 10;

    /// <summary>
    /// The widest the trail is drawn, at full pressure. Shared with the width
    /// floors below so that a preview of a weight cannot drift from the trail
    /// that weight actually produces.
    /// </summary>
    public const double MaximumTrailWidth = 9;

    public const uint TrailArgb = 0xFFE11D48;

    public static byte TrailRed => (byte)((TrailArgb >> 16) & 0xFF);

    public static byte TrailGreen => (byte)((TrailArgb >> 8) & 0xFF);

    public static byte TrailBlue => (byte)(TrailArgb & 0xFF);

    public double HoldSeconds { get; set; } = DefaultHoldSeconds;

    public double FadeSeconds { get; set; } = DefaultFadeSeconds;

    public LaserHoldMode HoldMode { get; set; } = LaserHoldMode.Shared;

    public LaserTrailWeight TrailWeight { get; set; } = LaserTrailWeight.Light;

    /// <summary>
    /// The narrowest the trail is ever drawn, whatever the pressure. The widest
    /// is the same for every weight, so raising this compresses the range from
    /// the bottom rather than making a firm stroke heavier.
    /// </summary>
    public static double MinimumTrailWidthFor(LaserTrailWeight weight) => weight switch
    {
        LaserTrailWeight.Bold => 4,
        LaserTrailWeight.Medium => 2.5,
        _ => 1.2,
    };

    /// <summary>
    /// The opacity a trail keeps at the lightest touch, as a fraction of the
    /// opacity a full-pressure stroke gets.
    /// </summary>
    public static double MinimumTrailOpacityFor(LaserTrailWeight weight) => weight switch
    {
        LaserTrailWeight.Bold => 0.85,
        LaserTrailWeight.Medium => 0.7,
        _ => 0.55,
    };

    public static LaserSettings Normalize(LaserSettings? settings)
    {
        var result = settings ?? new LaserSettings();
        result.HoldSeconds = Clamp(
            result.HoldSeconds,
            MinimumHoldSeconds,
            MaximumHoldSeconds,
            DefaultHoldSeconds);
        result.FadeSeconds = Clamp(
            result.FadeSeconds,
            MinimumFadeSeconds,
            MaximumFadeSeconds,
            DefaultFadeSeconds);
        if (!Enum.IsDefined(result.HoldMode))
        {
            result.HoldMode = LaserHoldMode.Shared;
        }

        if (!Enum.IsDefined(result.TrailWeight))
        {
            result.TrailWeight = LaserTrailWeight.Light;
        }

        return result;
    }

    private static double Clamp(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
