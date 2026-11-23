using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Finesse.Compatibility;

/// <summary>
/// Read-only Windows SetupAPI mapping from a staged/published oemNN.inf to its
/// actual DriverStore\FileRepository package. Port of frozen M191 validation.
/// No binding, staging, driver-store deletion or registry mutation.
/// </summary>
public static class M195WindowsDriverStoreLocator
{
    public sealed record LocatedPackage(
        string PublishedInf, string PublishedInfPath,
        string DriverStoreInfPath, string DriverStoreRoot,
        string InfSha256);

    public static LocatedPackage Locate(string publishedInf)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DriverStore mapping needs Windows.");
        string name = M195WindowsBindIdentity.RequirePublishedOemInf(publishedInf);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string published = Path.GetFullPath(Path.Combine(windows, "INF", name));
        if (!File.Exists(published))
            throw new FileNotFoundException("Published OEM INF does not exist.", published);
        RejectReparseAncestors(published);
        var buffer = new StringBuilder(32768);
        if (!SetupGetInfDriverStoreLocationW(published, IntPtr.Zero, IntPtr.Zero,
                buffer, (uint)buffer.Capacity, out uint required))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "M195_SETUPAPI_DRIVERSTORE_MAPPING_FAILED");
        if (required > buffer.Capacity)
            throw new InvalidDataException("M195_SETUPAPI_MAPPING_BUFFER_TRUNCATED");
        string mapped = Path.GetFullPath(buffer.ToString());
        string prefix = Path.GetFullPath(Path.Combine(windows, "System32",
            "DriverStore", "FileRepository")).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (!mapped.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(mapped))
            throw new InvalidDataException("M195_SETUPAPI_MAPPING_OUTSIDE_DRIVERSTORE");
        RejectReparseAncestors(mapped);
        string dir = Path.GetDirectoryName(mapped) ??
            throw new InvalidDataException("M195_SETUPAPI_DRIVERSTORE_NO_PARENT");
        return new LocatedPackage(name, published, mapped, dir, Sha(mapped));
    }

    /// <summary>
    /// Signed candidate-specific proof. Exactly the mapped "u0206030.inf"
    /// and the adjacent signed main CAT must match this transaction's bytes.
    /// A matching published oemNN.inf alone does not prove the Driver Store.
    /// </summary>
    public static LocatedPackage RequireFinesse26111Package(
        string publishedInf, string preparedOriginalInf, string signedCatalog,
        string signedCatalogSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedOriginalInf);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedCatalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(signedCatalogSha256);
        var located = Locate(publishedInf);
        if (!string.Equals(Path.GetFileName(located.DriverStoreInfPath),
                "u0206030.inf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_DRIVERSTORE_NOT_PINNED_26111_INF");
        if (!File.Exists(preparedOriginalInf) || Sha(preparedOriginalInf) != located.InfSha256)
            throw new InvalidDataException("M195_DRIVERSTORE_INF_NOT_SOURCE_IDENTICAL");
        string cat = Path.Combine(located.DriverStoreRoot, "u0206030.cat");
        if (!File.Exists(cat) ||
            !string.Equals(Sha(cat), signedCatalogSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(signedCatalog) ||
            !string.Equals(Sha(signedCatalog), signedCatalogSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_DRIVERSTORE_SIGNED_CAT_NOT_IDENTICAL");
        return located;
    }

    private static string Sha(string file)
    {
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void RejectReparseAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_SETUPAPI_REPARSE_PATH");
            string? parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupGetInfDriverStoreLocationW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupGetInfDriverStoreLocationW(
        [MarshalAs(UnmanagedType.LPWStr)] string infFileName,
        IntPtr alternatePlatform, IntPtr localeName,
        StringBuilder result, uint resultChars, out uint requiredChars);
}
