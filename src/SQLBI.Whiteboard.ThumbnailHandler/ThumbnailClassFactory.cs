using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SQLBI.Whiteboard.ThumbnailHandler;

[GeneratedComClass]
internal sealed partial class ThumbnailClassFactory : IClassFactory
{
    private const int Ok = 0;
    private const int NoAggregation = unchecked((int)0x80040110);
    private const int NoInterface = unchecked((int)0x80004002);

    private static readonly StrategyBasedComWrappers Wrappers = new();

    public unsafe int CreateInstance(nint outer, Guid* interfaceId, nint* result)
    {
        *result = 0;
        if (interfaceId is null)
        {
            return NoInterface;
        }

        if (outer != 0)
        {
            return NoAggregation;
        }

        try
        {
            var provider = new WboardThumbnailProvider();
            var unknown = Wrappers.GetOrCreateComInterfaceForObject(provider, CreateComInterfaceFlags.None);
            if (unknown == 0)
            {
                return NoInterface;
            }

            var hr = Marshal.QueryInterface(unknown, in *interfaceId, out var queried);
            Marshal.Release(unknown);
            *result = queried;
            return hr;
        }
        catch
        {
            return NoInterface;
        }
    }

    public int LockServer(bool @lock) => Ok;
}
