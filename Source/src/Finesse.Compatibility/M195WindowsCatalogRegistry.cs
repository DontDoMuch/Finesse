using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Finesse.Compatibility;

/// <summary>
/// READ-ONLY port of the proven frozen M115 native catalog database interop.
/// Enumerates the catalogs registered for an exact file hash under the Windows
/// driver verification subsystem (SHA-256). No catalog registration, removal,
/// certificate-store modification, GPU bind or reboot.
/// </summary>
public static class M195WindowsCatalogRegistry
{
    private static readonly Guid DriverActionVerify =
        new("F750E6C3-38EE-11D1-85E5-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CatalogInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string wszCatalogFile;
    }

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminAcquireContext2(
        out IntPtr context, ref Guid subsystem,
        [MarshalAs(UnmanagedType.LPWStr)] string algorithm,
        IntPtr strongHashPolicy, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminCalcHashFromFileHandle2(
        IntPtr context, IntPtr file, ref uint hashLength,
        [Out] byte[]? hash, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    private static extern IntPtr CryptCATAdminEnumCatalogFromHash(
        IntPtr context, [In] byte[] hash, uint hashLength,
        uint flags, ref IntPtr previous);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATCatalogInfoFromContext(
        IntPtr handle, ref CatalogInfo info, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseCatalogContext(
        IntPtr context, IntPtr info, uint flags);

    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseContext(
        IntPtr context, uint flags);

    public static IReadOnlyList<string> EnumerateForFile(
        string exactFile, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactFile);
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Catalog DB requires Windows.");
        string full = Path.GetFullPath(exactFile);
        if (!File.Exists(full))
            throw new FileNotFoundException("Catalog membership input missing.", full);
        RejectReparseAncestors(full);
        IntPtr admin = IntPtr.Zero;
        IntPtr current = IntPtr.Zero;
        var hits = new List<string>();
        try
        {
            Guid subsystem = DriverActionVerify;
            if (!CryptCATAdminAcquireContext2(out admin, ref subsystem,
                "SHA256", IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "M195_CRYPTCAT_ACQUIRE_CONTEXT2");
            using var input = new FileStream(full, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            uint count = 0;
            CryptCATAdminCalcHashFromFileHandle2(admin,
                input.SafeFileHandle.DangerousGetHandle(), ref count, null, 0);
            if (count is < 16 or > 128)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "M195_CRYPTCAT_CALC_HASH_LENGTH_INVALID");
            byte[] calculated = new byte[count];
            if (!CryptCATAdminCalcHashFromFileHandle2(admin,
                input.SafeFileHandle.DangerousGetHandle(), ref count,
                calculated, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "M195_CRYPTCAT_CALC_HASH_FAILED");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            current = CryptCATAdminEnumCatalogFromHash(admin,
                calculated, count, 0, ref current);
            while (current != IntPtr.Zero)
            {
                token.ThrowIfCancellationRequested();
                var details = new CatalogInfo {
                    cbStruct = (uint)Marshal.SizeOf<CatalogInfo>()
                };
                if (!CryptCATCatalogInfoFromContext(current, ref details, 0))
                    throw new Win32Exception(Marshal.GetLastWin32Error(),
                        "M195_CRYPTCAT_ENUM_CATALOG_INFO_FAILED");
                string catalog = Path.GetFullPath(details.wszCatalogFile ?? "");
                if (!File.Exists(catalog) || !seen.Add(catalog))
                    throw new InvalidDataException("M195_CRYPTCAT_INVALID_CATALOG_PATH");
                hits.Add(catalog);
                if (hits.Count > 1024)
                    throw new InvalidDataException("M195_CRYPTCAT_UNEXPECTED_MEMBERSHIP_COUNT");
                current = CryptCATAdminEnumCatalogFromHash(admin,
                    calculated, count, 0, ref current);
            }
            return hits.AsReadOnly();
        }
        finally
        {
            // Frozen M115 corrected v2c behavior: the continuation owns the
            // previous context. Do not manually release each member in-loop.
            if (admin != IntPtr.Zero && current != IntPtr.Zero)
                CryptCATAdminReleaseCatalogContext(admin, current, 0);
            if (admin != IntPtr.Zero)
                CryptCATAdminReleaseContext(admin, 0);
        }
    }

    public sealed record RegisteredMembership(
        string File, string OriginalSha256,
        bool AuthenticAmdCatalogRegistered, bool LocalFinesseCatalogRegistered,
        int RegisteredCatalogCount);

    public static RegisteredMembership ObserveDualMembership(
        string file, string expectedFileSha256, string originalCatalogSha256,
        string localCatalogSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        foreach (string sha in new[] {
            expectedFileSha256, originalCatalogSha256, localCatalogSha256 })
        {
            if (sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
                throw new InvalidDataException("M195_CRYPTCAT_INVALID_TARGET_DIGEST");
        }
        string originalBytesSha;
        using (var stream = File.OpenRead(file))
            originalBytesSha = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(originalBytesSha, expectedFileSha256,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_CRYPTCAT_CRITICAL_FILE_SHA_MISMATCH");
        IReadOnlyList<string> catalogs = EnumerateForFile(file);
        bool original = false, local = false;
        foreach (string path in catalogs)
        {
            string sha;
            using (var stream = File.OpenRead(path))
                sha = Convert.ToHexString(SHA256.HashData(stream));
            if (string.Equals(sha, originalCatalogSha256, StringComparison.OrdinalIgnoreCase))
                original = true;
            if (string.Equals(sha, localCatalogSha256, StringComparison.OrdinalIgnoreCase))
                local = true;
        }
        return new RegisteredMembership(file, originalBytesSha, original, local,
            catalogs.Count);
    }

    private static void RejectReparseAncestors(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_CRYPTCAT_REPARSE_FILE");
            string? parent = Path.GetDirectoryName(cursor);
            if (string.Equals(cursor, parent, StringComparison.OrdinalIgnoreCase))
                break;
            cursor = parent;
        }
    }
}
