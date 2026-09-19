namespace SQLBI.Whiteboard.Core.Settings;

/// <summary>
/// Which of the design-era controls the window offers. It is resolved once from
/// the settings and asked as one object, so a control is governed in the place
/// it is drawn rather than by each of them reading the mode and the four
/// switches for itself.
/// </summary>
/// <param name="DesignTools">
/// The Insert tab and its row, the palette and its pin, the toolbar Insert
/// button and the Select chevron, the shape, connector, and label tools, the
/// text editor of a shape or a label, the rotation handle and the quarter
/// turns, the connector handles, and the Anchors row.
/// </param>
/// <param name="PropertyBar">The bar above a selection, its rows and its menu.</param>
/// <param name="ExtendedSelection">
/// A tap selecting a stroke, the lasso, and the two Selection preferences.
/// </param>
/// <param name="DepthAndDuplicate">
/// Bring forward, Send backward, and Duplicate.
/// </param>
public sealed record FeatureSet(
    bool DesignTools,
    bool PropertyBar,
    bool ExtendedSelection,
    bool DepthAndDuplicate);

public static class Modes
{
    /// <summary>
    /// Design: the whole of 1.6.0.
    /// </summary>
    public static FeatureSet All { get; } = new(true, true, true, true);

    /// <summary>
    /// Teaching: the 1.5.2 board, plus what is invisible until it is used, and
    /// the extended selection with it. A tap on a stroke and the lasso are how
    /// somebody who only annotates picks up what they have just drawn, and
    /// neither puts anything on the screen to be in the way.
    /// </summary>
    public static FeatureSet Annotation { get; } = new(false, false, true, false);

    public static FeatureSet Resolve(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Mode switch
        {
            BoardMode.Teaching => Annotation,
            BoardMode.Design => All,
            _ => new FeatureSet(
                settings.DesignTools,
                settings.PropertyBar,
                settings.ExtendedSelection,
                settings.DepthAndDuplicate),
        };
    }

    /// <summary>
    /// What the View row's Design toggle leaves behind it: a press anywhere
    /// else goes to Design and remembers where it came from, and a press in
    /// Design comes back to that. Custom is therefore reached again by the same
    /// button that left it.
    /// </summary>
    public static (BoardMode Mode, BoardMode LastNonDesign) Toggle(
        BoardMode mode,
        BoardMode lastNonDesign)
    {
        if (mode != BoardMode.Design)
        {
            return (BoardMode.Design, mode);
        }

        // Design cannot be what Design returns to, so a settings file that says
        // so is read as the default rather than as a button that does nothing.
        BoardMode back = lastNonDesign == BoardMode.Design ? BoardMode.Teaching : lastNonDesign;
        return (back, back);
    }
}
