namespace SQLBI.Whiteboard.Core.Settings;

/// <summary>
/// What the pen's barrel button does while it is held. The list is meant to
/// grow: a new action needs an entry here, a choice in the settings catalog,
/// and a case wherever the window turns a held button into behaviour.
/// </summary>
public enum PenButtonAction
{
    Laser = 0,
    StraightLine = 1,
}

/// <summary>
/// The barrel button is the only one that can be assigned. Erasing belongs to
/// the reverse end of the pen, and to the upper side button, because Windows
/// describes both by inverting the stylus and no pen has been found that tells
/// the two apart - see TODO.md.
/// </summary>
public sealed class PenButtonSettings
{
    public PenButtonAction Barrel { get; set; } = PenButtonAction.Laser;

    public static PenButtonSettings Normalize(PenButtonSettings? settings)
    {
        var result = settings ?? new PenButtonSettings();
        if (!Enum.IsDefined(result.Barrel))
        {
            result.Barrel = PenButtonAction.Laser;
        }

        return result;
    }
}
