namespace SQLBI.Whiteboard.Core.Geometry;

/// <summary>
/// The barrel button can start a constraint only at the beginning of a physical
/// stroke. Shift remains a live modifier, independent of that choice.
/// </summary>
public sealed class PenStraightLineConstraint
{
    private bool _strokeStarted;
    private bool _barrelActive;

    // Call only for contact samples, never for a button press or a hover.
    // Repeated stylus downs from a barrel transition must not start a new stroke.
    public void Update(bool straightLineButtonDown)
    {
        if (!_strokeStarted)
        {
            _strokeStarted = true;
            _barrelActive = straightLineButtonDown;
        }
        else if (!straightLineButtonDown)
        {
            ReleaseButton();
        }
    }

    public bool IsActive(bool shiftDown) => shiftDown || _barrelActive;

    // A release disarms the rest of this stroke, even if another press arrives
    // before the next position sample. A release while hovering starts nothing.
    public void ReleaseButton() => _barrelActive = false;

    public void EndStroke()
    {
        _strokeStarted = false;
        _barrelActive = false;
    }
}
