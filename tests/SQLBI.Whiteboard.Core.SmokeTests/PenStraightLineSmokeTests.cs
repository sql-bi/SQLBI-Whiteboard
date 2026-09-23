using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.SmokeTests;

internal static class PenStraightLineSmokeTests
{
    public static void Run()
    {
        var constraint = new PenStraightLineConstraint();
        Assert(!constraint.IsActive(shiftDown: false), "An idle pen must not constrain ink.");

        constraint.Update(straightLineButtonDown: false);
        constraint.Update(straightLineButtonDown: true);
        constraint.Update(straightLineButtonDown: true);
        Assert(!constraint.IsActive(shiftDown: false),
            "A barrel press after contact, even before the first move, must leave the stroke freehand.");
        Assert(constraint.IsActive(shiftDown: true),
            "Shift must still constrain a stroke whose barrel press was ignored.");
        Assert(!constraint.IsActive(shiftDown: false),
            "Releasing Shift must restore freehand ink even while the ignored barrel press is held.");

        constraint.EndStroke();
        Assert(!constraint.IsActive(shiftDown: false), "A real lift must clear the constraint.");
        constraint.Update(straightLineButtonDown: true);
        Assert(constraint.IsActive(shiftDown: false),
            "A button held before contact must constrain the new stroke.");
        constraint.Update(straightLineButtonDown: true);
        Assert(constraint.IsActive(shiftDown: false),
            "Repeated contact samples and synthetic stylus downs must preserve the starting constraint.");

        constraint.Update(straightLineButtonDown: false);
        Assert(!constraint.IsActive(shiftDown: false),
            "A released button in a pen packet must remove the constraint.");
        constraint.Update(straightLineButtonDown: true);
        Assert(!constraint.IsActive(shiftDown: false),
            "Re-pressing during the same physical stroke must not restart Straight line.");

        constraint.EndStroke();
        constraint.Update(straightLineButtonDown: true);
        constraint.ReleaseButton();
        constraint.Update(straightLineButtonDown: true);
        Assert(!constraint.IsActive(shiftDown: false),
            "Release and re-press between position samples must also leave the rest of the stroke freehand.");
        Assert(constraint.IsActive(shiftDown: true),
            "Shift must work after a previously active barrel constraint has been released.");

        constraint.EndStroke();
        constraint.ReleaseButton();
        constraint.Update(straightLineButtonDown: true);
        Assert(constraint.IsActive(shiftDown: false),
            "A release while hovering must not prevent a later stroke from starting with the button held.");
        constraint.EndStroke();
        constraint.Update(straightLineButtonDown: false);
        Assert(!constraint.IsActive(shiftDown: false),
            "A new freehand stroke must not inherit the previous stroke's constraint.");

        constraint.EndStroke();
        constraint.EndStroke();
        Assert(!constraint.IsActive(shiftDown: false) && constraint.IsActive(shiftDown: true),
            "Ending ink for a tool change or mouse stroke must clear barrel state without changing Shift.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
