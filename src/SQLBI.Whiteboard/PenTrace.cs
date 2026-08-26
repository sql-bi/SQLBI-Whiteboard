using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;

namespace SQLBI.Whiteboard;

/// <summary>
/// Writes what the digitizer actually reports to a file, so the pen-button code
/// can be settled from facts rather than from inference. Windows describes the
/// upper side button and a pen turned round with the same Invert bit, and which
/// of the two carries a button event turns out to differ between devices - the
/// only way to know is to look.
///
/// Off unless SQLBI_WHITEBOARD_PENTRACE names a file to write to.
/// </summary>
internal static class PenTrace
{
    private static readonly object Gate = new();
    private static readonly string? Path =
        Environment.GetEnvironmentVariable("SQLBI_WHITEBOARD_PENTRACE");

    public static bool Enabled => !string.IsNullOrWhiteSpace(Path);

    public static void Note(string what, string detail)
    {
        if (!Enabled)
        {
            return;
        }

        Append($"{Milliseconds()} {what} {detail}");
    }

    public static void Write(string what, StylusEventArgs e, string state)
    {
        if (!Enabled)
        {
            return;
        }

        var device = e.StylusDevice;
        var line = new StringBuilder();
        line.Append(Milliseconds());
        line.Append(' ').Append(what);
        line.Append(" inverted=").Append(device.Inverted);
        line.Append(" inAir=").Append(device.InAir);
        line.Append(" tablet=").Append(device.TabletDevice.Type);

        if (e is StylusButtonEventArgs buttonArgs)
        {
            line.Append(" button='").Append(buttonArgs.StylusButton.Name).Append('\'');
            line.Append(" guid=").Append(buttonArgs.StylusButton.Guid);
        }

        foreach (StylusButton button in device.StylusButtons)
        {
            line.Append(" [").Append(button.Name).Append('=')
                .Append(button.StylusButtonState).Append(']');
        }

        var points = e.GetStylusPoints(e.Device.Target as System.Windows.IInputElement);
        if (points.Count > 0)
        {
            var point = points[^1];
            line.Append(" at=").Append(point.X.ToString("0")).Append(',')
                .Append(point.Y.ToString("0"));
            line.Append(" n=").Append(points.Count);
            line.Append(" pressure=").Append(point.PressureFactor.ToString("0.###"));
            Append(line, point, "tip", StylusPointProperties.TipButton);
            Append(line, point, "barrel", StylusPointProperties.BarrelButton);
            Append(line, point, "secondary", StylusPointProperties.SecondaryTipButton);
        }

        line.Append(' ').Append(state);
        Append(line.ToString());
    }

    private static long Milliseconds() =>
        Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);

    private static void Append(string line)
    {
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path!, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // A trace that cannot be written must not take the session with it.
            }
        }
    }

    private static void Append(
        StringBuilder line,
        StylusPoint point,
        string name,
        StylusPointProperty property)
    {
        if (point.HasProperty(property))
        {
            line.Append(' ').Append(name).Append('=')
                .Append(point.GetPropertyValue(property));
        }
    }
}
