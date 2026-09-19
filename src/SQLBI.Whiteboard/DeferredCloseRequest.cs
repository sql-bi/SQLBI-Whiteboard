using System.Windows.Threading;

namespace SQLBI.Whiteboard;

internal sealed class DeferredCloseRequest
{
    public bool IsPending { get; private set; }

    public async Task<bool> PrepareAsync(Func<Task<bool>> prepare)
    {
        if (IsPending) return false;
        IsPending = true;
        try
        {
            // Awaiting a save is not necessarily asynchronous: an empty session,
            // an unavailable session store, or a failure can finish immediately.
            // Always let WPF leave Closing before showing dialogs or retrying Close.
            await Dispatcher.Yield(DispatcherPriority.Background);
            return await prepare();
        }
        finally
        {
            IsPending = false;
        }
    }
}
