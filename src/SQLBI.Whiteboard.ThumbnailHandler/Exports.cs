using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SQLBI.Whiteboard.ThumbnailHandler;

internal static class Exports
{
    private const int ClassNotAvailable = unchecked((int)0x80040111);
    private const int Fail = unchecked((int)0x80004005);
    private const int False = 1;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static unsafe int DllGetClassObject(Guid* classId, Guid* interfaceId, nint* result)
    {
        *result = 0;
        try
        {
            if (*classId != WboardThumbnailProvider.Clsid)
            {
                return ClassNotAvailable;
            }

            var unknown = Wrappers.GetOrCreateComInterfaceForObject(
                new ThumbnailClassFactory(),
                CreateComInterfaceFlags.None);
            if (unknown == 0)
            {
                return Fail;
            }

            var hr = Marshal.QueryInterface(unknown, in *interfaceId, out var queried);
            Marshal.Release(unknown);
            *result = queried;
            return hr;
        }
        catch
        {
            return Fail;
        }
    }

    // Native AOT modules do not unload cleanly from Explorer. Stay loaded.
    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int DllCanUnloadNow() => False;
}
