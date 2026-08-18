using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SQLBI.Whiteboard.ThumbnailHandler;

internal static class ShellGuids
{
    // Same GUID as the shellex thumbnail-handler category. Explorer CreateInstance
    // requests this IID; IPreviewHandler is 8895b1c6-... and must not be used here.
    public const string ThumbnailProvider = "e357fccd-a995-4576-b01f-234630154e96";
    public const string InitializeWithStream = "b824b49d-22ac-4161-ac8a-9916e8fa3f7f";
    public const string ClassFactory = "00000001-0000-0000-C000-000000000046";
    public const string ThumbnailHandlerId = "7F3C1A2E-8D64-4B0F-9A51-E2C6D8B04715";
    public const string ThumbnailHandlerCategory = ThumbnailProvider;
}

[GeneratedComInterface]
[Guid(ShellGuids.InitializeWithStream)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IInitializeWithStream
{
    [PreserveSig]
    int Initialize(nint stream, uint mode);
}

[GeneratedComInterface]
[Guid(ShellGuids.ThumbnailProvider)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal partial interface IThumbnailProvider
{
    [PreserveSig]
    int GetThumbnail(uint cx, out nint bitmap, out int alphaType);
}

[GeneratedComInterface]
[Guid(ShellGuids.ClassFactory)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, Guid* interfaceId, nint* result);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool @lock);
}
