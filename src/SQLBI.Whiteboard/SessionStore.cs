using System.IO;
using SQLBI.Whiteboard.Core.Model;
using SQLBI.Whiteboard.Core.Persistence;

namespace SQLBI.Whiteboard;

/// <summary>
/// A session slot abandoned by a copy of the application that is no longer running, found
/// at startup and offered back.
/// </summary>
/// <param name="SlotId">Identifies the slot's three files.</param>
/// <param name="State">What the slot's sidecar said.</param>
/// <param name="BoardPath">The board copy, or null when the slot kept only a file name.</param>
internal sealed record AbandonedSession(string SlotId, SessionState State, string? BoardPath);

/// <summary>
/// Where a running copy of the application keeps the board it is holding, so that a crash or
/// an ordinary exit both leave something to come back to.
/// </summary>
/// <remarks>
/// Each running copy takes its own slot, named by a GUID and pinned by a lock file it holds
/// open until it exits. Two windows therefore never write to the same files, which is what
/// makes running two of them safe without the application having to forbid it. A slot whose
/// lock can be taken belonged to a copy that is gone; its sidecar says whether it left on
/// purpose.
/// </remarks>
internal sealed class SessionStore : IDisposable
{
    private const string BoardExtension = ".wboard";
    private const string StateExtension = ".json";
    private const string LockExtension = ".lock";

    /// <summary>
    /// How long an abandoned slot is kept. Long enough that a board lost to a crash is still
    /// there after a holiday, short enough that the folder does not grow without end.
    /// </summary>
    private static readonly TimeSpan AbandonedSlotLifetime = TimeSpan.FromDays(30);

    public static string FolderPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SQLBI",
        AppChannel.SettingsFolderName,
        "sessions");

    private readonly FileStream _lockStream;

    private SessionStore(string slotId, FileStream lockStream)
    {
        SlotId = slotId;
        _lockStream = lockStream;
    }

    public string SlotId { get; }

    private string BoardPath => PathFor(SlotId, BoardExtension);

    private string StatePath => PathFor(SlotId, StateExtension);

    /// <summary>
    /// Takes a fresh slot, or returns null when the folder cannot be written - a read-only
    /// profile, a locked-down machine. Autosave is a convenience, so failing to get one is
    /// never a reason to refuse to start.
    /// </summary>
    public static SessionStore? Acquire()
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var slotId = Guid.NewGuid().ToString("n");
            var lockStream = new FileStream(
                PathFor(slotId, LockExtension),
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            return new SessionStore(slotId, lockStream);
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

    /// <summary>
    /// Writes the sidecar, and the board beside it when <paramref name="document"/> is given.
    /// Passing null keeps the slot pointing at a file without carrying a copy of it, which is
    /// what an unmodified board and a discarded one both want.
    /// </summary>
    public async Task WriteAsync(
        BoardDocument? document,
        SessionState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (document is null)
        {
            Delete(BoardPath);
        }
        else
        {
            // Written beside the destination and moved over it, so a copy interrupted
            // half way through cannot be what a recovery later reads.
            var temporaryPath = BoardPath + ".tmp";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true))
            {
                await BoardArchive.SaveAsync(document, stream, cancellationToken);
            }

            File.Move(temporaryPath, BoardPath, overwrite: true);
        }

        state.WrittenUtc = DateTimeOffset.UtcNow;
        var temporaryStatePath = StatePath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryStatePath,
            SessionState.Format(state),
            cancellationToken);
        File.Move(temporaryStatePath, StatePath, overwrite: true);
    }

    /// <summary>
    /// Every slot no copy of the application is holding any more, newest first.
    /// </summary>
    public static IReadOnlyList<AbandonedSession> FindAbandoned()
    {
        var found = new List<AbandonedSession>();
        try
        {
            if (!Directory.Exists(FolderPath))
            {
                return found;
            }

            foreach (var statePath in Directory.EnumerateFiles(FolderPath, "*" + StateExtension))
            {
                var slotId = Path.GetFileNameWithoutExtension(statePath);
                if (!IsAbandoned(slotId))
                {
                    continue;
                }

                var state = ReadState(statePath);
                if (state is null)
                {
                    continue;
                }

                var boardPath = PathFor(slotId, BoardExtension);
                found.Add(new AbandonedSession(
                    slotId,
                    state,
                    File.Exists(boardPath) ? boardPath : null));
            }
        }
        catch (IOException)
        {
            return found;
        }
        catch (UnauthorizedAccessException)
        {
            return found;
        }

        found.Sort(static (left, right) => right.State.WrittenUtc.CompareTo(left.State.WrittenUtc));
        return found;
    }

    /// <summary>
    /// Removes abandoned slots that have outlived <see cref="AbandonedSlotLifetime"/>, and
    /// any whose sidecar could not be read at all.
    /// </summary>
    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(FolderPath))
            {
                return;
            }

            var cutoff = DateTimeOffset.UtcNow - AbandonedSlotLifetime;
            foreach (var statePath in Directory.EnumerateFiles(FolderPath, "*" + StateExtension))
            {
                var slotId = Path.GetFileNameWithoutExtension(statePath);
                if (!IsAbandoned(slotId))
                {
                    continue;
                }

                var state = ReadState(statePath);
                if (state is null || state.WrittenUtc < cutoff)
                {
                    Forget(slotId);
                }
            }

            // A lock with no sidecar beside it is a slot that held nothing worth keeping.
            // Dispose tries to take its own away on the way out and cannot always be sure
            // of it, so the next start sweeps up whatever was left - one empty file per
            // launch otherwise accumulates for as long as the application is installed.
            foreach (var lockPath in Directory.EnumerateFiles(FolderPath, "*" + LockExtension))
            {
                var slotId = Path.GetFileNameWithoutExtension(lockPath);
                if (!File.Exists(PathFor(slotId, StateExtension)) && IsAbandoned(slotId))
                {
                    Delete(lockPath);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Deletes a slot's three files.</summary>
    public static void Forget(string slotId)
    {
        Delete(PathFor(slotId, BoardExtension));
        Delete(PathFor(slotId, StateExtension));
        Delete(PathFor(slotId, LockExtension));
    }

    /// <summary>
    /// Drops what this slot was holding while keeping the slot itself, for a session with
    /// nothing worth coming back to. The lock file cannot be removed here because this copy
    /// still has it open; <see cref="Dispose"/> clears it up on the way out.
    /// </summary>
    public void Clear()
    {
        Delete(BoardPath);
        Delete(StatePath);
    }

    /// <summary>
    /// Releases the lock. A slot still holding a sidecar keeps its files on purpose - they
    /// are what the next start reads, whether this exit was an orderly one or not - and one
    /// that was cleared takes its lock file with it rather than leaving it behind.
    /// </summary>
    public void Dispose()
    {
        _lockStream.Dispose();
        if (!File.Exists(StatePath))
        {
            Delete(PathFor(SlotId, LockExtension));
        }
    }

    /// <summary>
    /// Whether two paths name the same file on this machine.
    /// </summary>
    /// <remarks>
    /// A board reaches the application as a full path from Explorer, as whatever was typed
    /// on a command line, and as whatever a sidecar recorded when it was last open, so the
    /// two sides are expanded before they are compared. <see cref="Path.GetFullPath(string)"/>
    /// settles all of it, 8.3 short components included: it expands those against the
    /// directory entries, so a slot holding <c>C:\MARCOR~1\board.wboard</c> does match the
    /// same board opened by its long name. That only holds while the file is there to be
    /// read, which is exactly when this is asked - the board being opened exists, and the
    /// slot that matches it names that same file.
    /// </remarks>
    public static bool IsSameFile(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static string PathFor(string slotId, string extension) =>
        Path.Combine(FolderPath, slotId + extension);

    /// <summary>
    /// Whether the slot's lock can be taken, which is the only reliable way to ask whether
    /// the copy of the application that owned it is still running. A process id would not
    /// survive the id being reused.
    /// </summary>
    private static bool IsAbandoned(string slotId)
    {
        var lockPath = PathFor(slotId, LockExtension);
        if (!File.Exists(lockPath))
        {
            return true;
        }

        try
        {
            using var stream = new FileStream(
                lockPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static SessionState? ReadState(string statePath)
    {
        try
        {
            return SessionState.Parse(File.ReadAllText(statePath));
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

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
