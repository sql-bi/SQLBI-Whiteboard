namespace SQLBI.Whiteboard.Core.Settings;

/// <summary>
/// Names Windows and tablet drivers put on stylus buttons, used to pick the
/// barrel button out of the list a device reports. The writing tip is never it,
/// and neither is the reverse end - which drivers call "Eraser", and sometimes
/// "Secondary", "Upper" or "2".
/// </summary>
public static class PenBarrelButton
{
    public static bool IsWritingTipName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.Contains("Tip", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("Secondary", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("Eraser", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReverseEndName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        return name.Contains("Eraser", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Upper", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Secondary", StringComparison.OrdinalIgnoreCase) ||
               name.Contains('2', StringComparison.Ordinal);
    }
}
