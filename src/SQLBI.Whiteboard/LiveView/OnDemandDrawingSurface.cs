using System.Reflection;
using System.Windows;
using Vortice.Wpf;

namespace SQLBI.Whiteboard.LiveView;

/// <summary>
/// Keeps Vortice.Wpf 3.8.3's DrawingSurface on demand. That version leaves its
/// refresh flag set after rendering, which otherwise dirties and flushes the
/// D3DImage on every WPF composition frame even when no capture frame arrived.
/// </summary>
internal sealed class OnDemandDrawingSurface : DrawingSurface
{
    private static readonly FieldInfo? ContentNeedsRefreshField =
        typeof(DrawingSurface).GetField(
            "_contentNeedsRefresh",
            BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? WindowClosedMethod =
        typeof(DrawingSurface).GetMethod(
            "Window_Closed",
            BindingFlags.Instance | BindingFlags.NonPublic);

    private Window? _ownerWindow;

    public OnDemandDrawingSurface()
    {
        Loaded += (_, _) => _ownerWindow = Window.GetWindow(this);
    }

    public void CompleteRefresh()
    {
        ContentNeedsRefreshField?.SetValue(this, false);
    }

    /// <summary>
    /// Vortice keeps a private Window.Closed handler after this surface is
    /// unloaded. Remove that handler before taking the surface out of the
    /// visual tree so its D3D resources are not torn down a second time when
    /// the application window later closes.
    /// </summary>
    public void PrepareForRemoval()
    {
        if (_ownerWindow is null || WindowClosedMethod is null)
        {
            return;
        }

        var handler = (EventHandler)Delegate.CreateDelegate(
            typeof(EventHandler),
            this,
            WindowClosedMethod);
        _ownerWindow.Closed -= handler;
        _ownerWindow = null;
    }
}
