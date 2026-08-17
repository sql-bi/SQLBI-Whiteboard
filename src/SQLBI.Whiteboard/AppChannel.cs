using System.IO;

namespace SQLBI.Whiteboard;

/// <summary>
/// Identifies which download channel this copy was installed from. The released channel
/// ships no marker; pre-release installers place a <c>channel.txt</c> beside the executable
/// containing the channel name.
/// </summary>
/// <remarks>
/// Detected at run time rather than compiled in, so one build of the application serves
/// every channel and a tested binary can be promoted without being rebuilt.
/// </remarks>
internal static class AppChannel
{
    private const string MarkerFileName = "channel.txt";

    /// <summary>Channel name, or null when this is the released channel.</summary>
    public static string? Name { get; } = ReadName();

    /// <summary>Folder under %APPDATA%\SQLBI holding this channel's settings, kept separate
    /// so a pre-release copy cannot overwrite the settings of a released one.</summary>
    public static string SettingsFolderName =>
        Name is null ? "Whiteboard" : $"Whiteboard {Name}";

    /// <summary>Appended to the window title so two installed copies are distinguishable.</summary>
    public static string WindowTitleSuffix =>
        Name is null ? string.Empty : $" ({Name})";

    private static string? ReadName()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, MarkerFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var value = File.ReadAllText(path).Trim();
            return value.Length == 0 ? null : value;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
