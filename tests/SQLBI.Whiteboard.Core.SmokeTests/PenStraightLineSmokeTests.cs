using SQLBI.Whiteboard.Core.Geometry;

namespace SQLBI.Whiteboard.Core.SmokeTests;

internal static class PenStraightLineSmokeTests
{
    public static void Run()
    {
        // A pen button mapped to Shift must follow the same rule as a native
        // barrel button, including when it is pressed before the first move.
        foreach (var barrel in new[] { true, false })
        {
            var shift = !barrel;
            var name = barrel ? "Barrel" : "Shift";
            var constraint = new PenStraightLineConstraint();
            Assert(!constraint.IsActive, $"{name}: an idle pen must not constrain ink.");

            constraint.Update(straightLineButtonDown: false, shiftDown: false);
            constraint.Update(barrel, shift);
            constraint.Update(barrel, shift);
            Assert(!constraint.IsActive,
                $"{name}: pressing after contact must leave the stroke freehand.");

            constraint.EndStroke();
            constraint.Update(barrel, shift);
            Assert(constraint.IsActive,
                $"{name}: held before contact must constrain the new stroke.");
            constraint.Update(barrel, shift);
            Assert(constraint.IsActive,
                $"{name}: repeated samples and synthetic stylus downs must preserve the starting constraint.");

            constraint.Update(straightLineButtonDown: false, shiftDown: false);
            Assert(!constraint.IsActive, $"{name}: releasing must remove the constraint.");
            constraint.Update(barrel, shift);
            Assert(!constraint.IsActive,
                $"{name}: re-pressing during the same stroke must not restart Straight line.");

            constraint.EndStroke();
            constraint.Update(barrel, shift);
            if (barrel)
            {
                constraint.ReleaseButton();
            }
            else
            {
                constraint.ReleaseShift();
            }
            constraint.Update(barrel, shift);
            Assert(!constraint.IsActive,
                $"{name}: release/re-press between position samples must also disarm the rest of the stroke.");

            constraint.EndStroke();
            constraint.ReleaseButton();
            constraint.ReleaseShift();
            constraint.Update(barrel, shift);
            Assert(constraint.IsActive,
                $"{name}: release while hovering must not prevent the next stroke from being constrained.");
            constraint.EndStroke();
            constraint.Update(straightLineButtonDown: false, shiftDown: false);
            Assert(!constraint.IsActive, $"{name}: a new freehand stroke must not inherit the constraint.");
            constraint.EndStroke();
            constraint.EndStroke();
            Assert(!constraint.IsActive, $"{name}: a lift or tool change must clear the constraint.");
        }

        var both = new PenStraightLineConstraint();
        both.Update(straightLineButtonDown: true, shiftDown: true);
        both.ReleaseShift();
        Assert(both.IsActive, "Releasing Shift must preserve the barrel button held at stroke start.");
        both.ReleaseButton();
        Assert(!both.IsActive, "Releasing both original modifiers must restore freehand ink.");
        both.Update(straightLineButtonDown: true, shiftDown: true);
        Assert(!both.IsActive, "Neither modifier may rearm the other during the same stroke.");

        both.EndStroke();
        both.Update(straightLineButtonDown: true, shiftDown: true);
        both.ReleaseButton();
        Assert(both.IsActive, "Releasing the barrel button must preserve Shift held at stroke start.");
        both.ReleaseShift();
        Assert(!both.IsActive, "The remaining Shift constraint must end on release.");

        both.EndStroke();
        both.Update(straightLineButtonDown: true, shiftDown: false);
        both.Update(straightLineButtonDown: true, shiftDown: true);
        both.Update(straightLineButtonDown: false, shiftDown: true);
        Assert(!both.IsActive, "A mid-stroke Shift press must not take over from the barrel constraint.");

        both.EndStroke();
        both.Update(straightLineButtonDown: false, shiftDown: true);
        both.Update(straightLineButtonDown: true, shiftDown: true);
        both.Update(straightLineButtonDown: true, shiftDown: false);
        Assert(!both.IsActive, "A mid-stroke barrel press must not take over from the Shift constraint.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
