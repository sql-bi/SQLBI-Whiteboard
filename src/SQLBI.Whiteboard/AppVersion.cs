using System.Reflection;

namespace SQLBI.Whiteboard;

internal static class AppVersion
{
    public static string Informational { get; } = Read();

    private static string Read()
    {
        var informational = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
