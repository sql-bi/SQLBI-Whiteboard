namespace SQLBI.Whiteboard.Core.Geometry;

/// <summary>
/// Shift and the barrel button can start a constraint only at the beginning of
/// a physical stroke. Releasing either disables its contribution until the lift.
/// </summary>
public sealed class PenStraightLineConstraint
{
    private bool _strokeStarted;
    private bool _barrelActive;
    private bool _shiftActive;

    // Call only for contact samples, never for a button press or a hover.
    // Repeated stylus downs from a barrel transition must not start a new stroke.
    public void Update(bool straightLineButtonDown, bool shiftDown)
    {
        if (!_strokeStarted)
        {
            _strokeStarted = true;
            _barrelActive = straightLineButtonDown;
            _shiftActive = shiftDown;
        }
        else
        {
            _barrelActive &= straightLineButtonDown;
            _shiftActive &= shiftDown;
        }
    }

    public bool IsActive => _shiftActive || _barrelActive;

    // A release disarms the rest of this stroke, even if another press arrives
    // before the next position sample. A release while hovering starts nothing.
    public void ReleaseButton() => _barrelActive = false;

    public void ReleaseShift() => _shiftActive = false;

    public void EndStroke()
    {
        _strokeStarted = false;
        _barrelActive = false;
        _shiftActive = false;
    }
}
