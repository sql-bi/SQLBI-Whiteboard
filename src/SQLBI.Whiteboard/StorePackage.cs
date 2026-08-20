namespace SQLBI.Whiteboard;

internal static class StorePackage
{
    public static bool IsStoreInstall { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.SignatureKind
                == Windows.ApplicationModel.PackageSignatureKind.Store;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
