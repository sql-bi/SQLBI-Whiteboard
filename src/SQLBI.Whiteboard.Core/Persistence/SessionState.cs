using System.Text.Json;
using System.Text.Json.Serialization;

namespace SQLBI.Whiteboard.Core.Persistence;

/// <summary>
/// What a session slot knows about the board it is holding, written beside the board copy
/// itself. One slot belongs to one running copy of the application, so two windows never
/// write to the same pair of files.
/// </summary>
public sealed class SessionState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// The <c>.wboard</c> this session was editing, or null for a board that has never been
    /// saved. A restored session takes this back, so Save still writes where it used to.
    /// </summary>
    public string? BoardPath { get; set; }

    /// <summary>
    /// Whether the board copy beside this file differs from <see cref="BoardPath"/>. False
    /// means the file on disk is the better copy and the board beside this file is stale -
    /// which is how Discard and a plain Save both leave the slot.
    /// </summary>
    public bool Modified { get; set; }

    /// <summary>
    /// False until the window closes through its own closing path. A slot still reading
    /// false when nothing holds its lock any more belonged to a copy that crashed, and is
    /// what turns a silent restore into an offer of recovery.
    /// </summary>
    public bool ExitedCleanly { get; set; }

    public DateTimeOffset WrittenUtc { get; set; } = DateTimeOffset.UtcNow;

    public double CameraCenterX { get; set; }

    public double CameraCenterY { get; set; }

    public double CameraZoom { get; set; } = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Format(SessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, JsonOptions);
    }

    /// <summary>
    /// Reads a sidecar, returning null for anything that cannot be understood. A slot that
    /// will not parse is a slot to leave alone, never one to throw over: the application is
    /// starting up, and a damaged recovery file must not be what stops it.
    /// </summary>
    public static SessionState? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
            if (state is null || state.Version is < 1 or > CurrentVersion)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(state.BoardPath))
            {
                state.BoardPath = null;
            }

            return state;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
