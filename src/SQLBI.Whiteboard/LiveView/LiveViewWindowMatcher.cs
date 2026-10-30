using System.Diagnostics;

namespace SQLBI.Whiteboard.LiveView;

internal readonly record struct LiveViewCaptureDescription(string Title, int Width, int Height);

internal static class LiveViewWindowMatcher
{
    public static LiveViewWindowTarget? Match(
        LiveViewCaptureDescription selected,
        IEnumerable<LiveViewWindowTarget> candidates,
        Func<LiveViewWindowTarget, LiveViewCaptureDescription> describe)
    {
        if (string.IsNullOrWhiteSpace(selected.Title) || selected.Width <= 0 || selected.Height <= 0)
            return null;
        try
        {
            LiveViewWindowTarget? match = null;
            foreach (var candidate in candidates)
            {
                if (!string.Equals(candidate.Title, selected.Title, StringComparison.Ordinal)) continue;
                if (describe(candidate) != selected) continue;
                if (match is not null) return null;
                match = candidate;
            }
            return match;
        }
        catch (Exception exception)
        {
            // A candidate disappearing or becoming inaccessible makes identification uncertain.
            Debug.WriteLine($"[LiveView] Could not compare capture descriptions: {exception}");
            return null;
        }
    }
}
