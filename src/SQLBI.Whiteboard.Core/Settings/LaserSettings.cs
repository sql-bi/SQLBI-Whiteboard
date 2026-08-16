namespace SQLBI.Whiteboard.Core.Settings;

public enum LaserHoldMode
{
    Shared = 0,
    PerStroke = 1,
}

public sealed class LaserSettings
{
    public const double DefaultHoldSeconds = 2;
    public const double DefaultFadeSeconds = 0.85;
    public const double MinimumHoldSeconds = 0;
    public const double MaximumHoldSeconds = 30;
    public const double MinimumFadeSeconds = 0.05;
    public const double MaximumFadeSeconds = 10;

    public double HoldSeconds { get; set; } = DefaultHoldSeconds;

    public double FadeSeconds { get; set; } = DefaultFadeSeconds;

    public LaserHoldMode HoldMode { get; set; } = LaserHoldMode.Shared;

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

        return result;
    }

    private static double Clamp(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
