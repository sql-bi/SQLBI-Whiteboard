using System.IO;
using SQLBI.Whiteboard.Core.Settings;

namespace SQLBI.Whiteboard;

internal static class AppSettingsStore
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SQLBI",
        AppChannel.SettingsFolderName,
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            return AppSettingsSerializer.Parse(File.ReadAllText(FilePath));
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, AppSettingsSerializer.Format(settings));
        File.Copy(temporaryPath, FilePath, overwrite: true);
        File.Delete(temporaryPath);
    }
}
