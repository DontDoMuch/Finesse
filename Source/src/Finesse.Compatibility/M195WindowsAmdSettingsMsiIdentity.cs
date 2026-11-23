using System.Runtime.InteropServices;
using System.Text;

namespace Finesse.Compatibility;

/// <summary>
/// Read-only MSI database and installed product inspection; no MSI mutation.
/// Native identity is compared to the independent, pinned M169 source hash.
/// </summary>
public static class M195WindowsAmdSettingsMsiIdentity
{
    private const uint ErrorNoMoreItems = 259;
    private const uint ErrorMoreData = 234;

    public sealed record PackageIdentity(string ProductCode, string UpgradeCode,
        string ProductName, string ProductVersion, string Manufacturer);

    public static void RequirePinned26111(PackageIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!string.Equals(identity.ProductCode, M195AmdSettings26111.ProductCode,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(identity.UpgradeCode, M195AmdSettings26111.UpgradeCode,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(identity.ProductName, M195AmdSettings26111.ProductName,
                StringComparison.Ordinal) ||
            !string.Equals(identity.ProductVersion, M195AmdSettings26111.ProductVersion,
                StringComparison.Ordinal) ||
            !identity.Manufacturer.Contains(M195AmdSettings26111.PublisherContains,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_M169_MSI_PROPERTY_IDENTITY_MISMATCH");
    }

    public static PackageIdentity ReadExactMsiProperties(string msi)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("MSI native metadata requires Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(msi);
        string path = Path.GetFullPath(msi);
        if (!File.Exists(path))
            throw new FileNotFoundException("Nested AMD Settings MSI not found.", path);
        // Persist=0 is MSIDBOPEN_READONLY; the database is NEVER modified.
        uint code = MsiOpenDatabase(path, IntPtr.Zero, out IntPtr db);
        if (code != 0 || db == IntPtr.Zero)
            throw new InvalidDataException("M195_M169_OPEN_MSI_FAILED_" + code);
        try
        {
            string Field(string field)
            {
                // These hard-coded property names cannot contain SQL tokens.
                string sql = "SELECT `Value` FROM `Property` WHERE `Property`='" +
                    field + "'";
                uint status = MsiDatabaseOpenView(db, sql, out IntPtr view);
                if (status != 0 || view == IntPtr.Zero)
                    throw new InvalidDataException("M195_M169_OPEN_MSI_VIEW_FAILED_" + field + "_" + status);
                try
                {
                    status = MsiViewExecute(view, IntPtr.Zero);
                    if (status != 0)
                        throw new InvalidDataException("M195_M169_QUERY_MSI_FAILED_" + field + "_" + status);
                    status = MsiViewFetch(view, out IntPtr record);
                    if (status != 0 || record == IntPtr.Zero)
                        throw new InvalidDataException("M195_M169_MSI_PROPERTY_MISSING_" + field);
                    try
                    {
                        uint size = 4096;
                        var sb = new StringBuilder(4097);
                        status = MsiRecordGetString(record, 1, sb, ref size);
                        if (status != 0 || size is 0 or > 4096)
                            throw new InvalidDataException("M195_M169_MSI_PROPERTY_READ_FAILED_" + field);
                        return sb.ToString();
                    }
                    finally { MsiCloseHandle(record); }
                }
                finally { MsiCloseHandle(view); }
            }
            return new PackageIdentity(Field("ProductCode"), Field("UpgradeCode"),
                Field("ProductName"), Field("ProductVersion"),
                Field("Manufacturer"));
        }
        finally { MsiCloseHandle(db); }
    }

    public static bool ExactTargetInstalled() =>
        OperatingSystem.IsWindows() &&
        MsiQueryProductState(M195AmdSettings26111.ProductCode) == 5;

    public static IReadOnlyList<M195AmdSettings26111.RegisteredProduct>
        EnumerateRelatedDesktopProducts()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Related MSI inventory requires Windows.");
        var matches = new List<M195AmdSettings26111.RegisteredProduct>();
        for (uint index = 0; index < 100; index++)
        {
            var buffer = new StringBuilder(39);
            uint status = MsiEnumRelatedProducts(
                M195AmdSettings26111.UpgradeCode, 0, index, buffer);
            if (status == ErrorNoMoreItems)
                return matches.AsReadOnly();
            if (status != 0)
                throw new InvalidDataException("M195_M184_ENUM_RELATED_MSI_FAILED_" + status);
            string code = buffer.ToString();
            if (!Guid.TryParseExact(code, "B", out Guid parsed))
                throw new InvalidDataException("M195_M184_RELATED_MSI_BAD_GUID");
            string normalized = parsed.ToString("B").ToUpperInvariant();
            int state = MsiQueryProductState(normalized);
            if (state != 5) continue;
            matches.Add(new M195AmdSettings26111.RegisteredProduct(
                normalized, ProductInfo(normalized, "ProductName"),
                ProductInfo(normalized, "Publisher"),
                ProductInfo(normalized, "VersionString"),
                true, true));
        }
        throw new InvalidDataException("M195_M184_RELATED_PRODUCTS_LIMIT_EXCEEDED");
    }

    private static string ProductInfo(string code, string field)
    {
        var text = new StringBuilder(4096);
        uint length = 4095;
        uint status = MsiGetProductInfo(code, field, text, ref length);
        if (status == ErrorMoreData)
            throw new InvalidDataException("M195_M184_MSI_REGISTRY_VALUE_TOO_LARGE");
        if (status != 0)
            throw new InvalidDataException("M195_M184_MSI_REGISTRY_QUERY_FAILED_" + field + "_" + status);
        return text.ToString();
    }

    [DllImport("msi.dll", EntryPoint = "MsiOpenDatabaseW", CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabase(string path, IntPtr persist, out IntPtr handle);
    [DllImport("msi.dll", EntryPoint = "MsiDatabaseOpenViewW", CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenView(IntPtr db, string query, out IntPtr view);
    [DllImport("msi.dll", EntryPoint = "MsiViewExecute")]
    private static extern uint MsiViewExecute(IntPtr view, IntPtr record);
    [DllImport("msi.dll", EntryPoint = "MsiViewFetch")]
    private static extern uint MsiViewFetch(IntPtr view, out IntPtr record);
    [DllImport("msi.dll", EntryPoint = "MsiRecordGetStringW", CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetString(IntPtr record, uint field,
        StringBuilder buffer, ref uint length);
    [DllImport("msi.dll", EntryPoint = "MsiCloseHandle")]
    private static extern uint MsiCloseHandle(IntPtr handle);
    [DllImport("msi.dll", EntryPoint = "MsiEnumRelatedProductsW", CharSet = CharSet.Unicode)]
    private static extern uint MsiEnumRelatedProducts(string upgrade, uint reserved,
        uint index, StringBuilder productCode);
    [DllImport("msi.dll", EntryPoint = "MsiGetProductInfoW", CharSet = CharSet.Unicode)]
    private static extern uint MsiGetProductInfo(string code, string field,
        StringBuilder value, ref uint length);
    [DllImport("msi.dll", EntryPoint = "MsiQueryProductStateW", CharSet = CharSet.Unicode)]
    private static extern int MsiQueryProductState(string code);
}
