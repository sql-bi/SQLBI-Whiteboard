namespace SQLBI.Whiteboard.LiveView;

/// <summary>
/// A temporary focus pause must never overwrite the user's Play/Pause choice.
/// Only IsFrozen is saved with the board; suspension lasts for this session.
/// </summary>
internal sealed class LiveViewPlaybackState
{
    public bool IsFrozen { get; set; }

    public bool IsSuspended { get; private set; }

    public bool CanCapture => !IsFrozen && !IsSuspended;

    public bool SetSuspended(bool suspended)
    {
        if (IsSuspended == suspended)
        {
            return false;
        }

        IsSuspended = suspended;
        return true;
    }
}
