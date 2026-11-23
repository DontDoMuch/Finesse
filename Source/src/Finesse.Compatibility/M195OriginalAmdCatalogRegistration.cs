using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// M194 M191/M115 narrow remediation: register ONLY the authentic pinned
/// original AMD 26.11.1 WHCP CAT, and only if the REAL DriverStore's 14
/// critical AMD graphics bytes have been authenticated in that catalog.
/// This intentionally does not register the locally signed catalog, stage
/// drivers, alter cert stores, bind the GPU, or reboot.
/// INTERNAL: no GUI wiring before complete M195 signing/bind/rollback.
/// </summary>
internal static class M195OriginalAmdCatalogRegistration
{
    private const string ManagedName = "Finesse-AMD-26111-Official-WHCP.cat";
    private static readonly Guid DriverActionVerify =
        new("F750E6C3-38EE-11D1-85E5-00C04FC295EE");

    internal sealed record Result(
        bool RegistrationWasNeeded, bool RegistrationMutated,
        bool OriginalCoverage14Of14,
        bool RegisteredOriginal14Of14,
        bool DualTrust14Of14,
        string Action, string BeforeEvidence,
        string AfterEvidence, string ReceiptFile);

    /// <summary>
    /// The ONLY mutating method. It first performs a fresh, independently
    /// measured M191 read-only audit and remeasures after AddCatalog. Preexisting
    /// Microsoft catalog database files are never overwritten or removed.
    /// </summary>
    internal static async Task<Result> EnsureOriginalRegisteredAsync(
        M195LifecycleSequence.Device expectedDevice,
        string publishedInf, string preparedCandidateRoot, string originalAmdCatalog,
        string signedLocalCatalogSha256, string verifiedSignToolExe,
        string freshEvidenceDirectory, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Original catalog registration requires Windows.");
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("Use the existing elevated Finesse installer process.");

        string folder = Path.GetFullPath(freshEvidenceDirectory);
        if (Directory.Exists(folder) || File.Exists(folder))
            throw new IOException("M195_REGISTRATION_REQUIRES_FRESH_EVIDENCE_FOLDER");
        string parent = Path.GetDirectoryName(folder) ??
            throw new InvalidDataException("M195_REGISTRATION_EVIDENCE_PARENT_MISSING");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("Catalog registration evidence parent is missing.");
        Directory.CreateDirectory(folder);
        string before = Path.Combine(folder, "M195-DualTrust-Before.json");
        string after = Path.Combine(folder, "M195-DualTrust-After.json");
        string receipt = Path.Combine(folder, "M195-OfficialCatalogRegistration.json");

        var previous = await M195WindowsDualCatalogAudit.CaptureAsync(
            expectedDevice, publishedInf, preparedCandidateRoot, originalAmdCatalog,
            signedLocalCatalogSha256, verifiedSignToolExe, before, token);
        var d = previous.Assessment;
        if (d.OriginalCoverage != 14 || !previous.Snapshot.OriginalCatalogAuthenticodeWhcp ||
            !previous.Snapshot.OriginalCatalogKernelPolicy)
            throw new InvalidDataException(
                "M195_REGISTRATION_ORIGINAL_WHCP_NOT_AUTHENTICATED_14_OF_14");

        string action = "ALREADY_REGISTERED_ALL_14";
        bool mutated = false;
        if (d.RegisteredOriginalCoverage != 14)
        {
            string path = Path.GetFullPath(originalAmdCatalog);
            string pinned = M195LiveSigningPreflight.PinnedOriginalAmdMainCatalogSha256;
            if (Sha(path) != pinned)
                throw new InvalidDataException("M195_REGISTRATION_SOURCE_CAT_CHANGED");

            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string managed = Path.Combine(windows, "System32", "CatRoot",
                "{" + DriverActionVerify.ToString().ToUpperInvariant() + "}", ManagedName);
            if (File.Exists(managed))
            {
                if (Sha(managed) != pinned)
                    throw new InvalidDataException("M195_REGISTRATION_MANAGED_NAME_COLLISION");
                action = "EXISTING_MANAGED_CATALOG_SAME_SHA";
            }
            else
            {
                token.ThrowIfCancellationRequested();
                string installed = AddExactlyOneCatalog(path, ManagedName);
                mutated = true;
                if (Sha(installed) != pinned)
                    throw new InvalidDataException("M195_REGISTRATION_INSTALLED_CAT_HASH_MISMATCH");
                action = "REGISTERED_PINNED_ORIGINAL_AMD_WHCP_CAT";
            }
        }

        // Re-enumerate every critical member: successful AddCatalog alone is
        // insufficient; each installed AMD file must resolve to this CAT.
        var following = await M195WindowsDualCatalogAudit.CaptureAsync(
            expectedDevice, publishedInf, preparedCandidateRoot, originalAmdCatalog,
            signedLocalCatalogSha256, verifiedSignToolExe, after, token);
        var done = following.Assessment;
        string summary = JsonSerializer.Serialize(new {
            Schema = "Finesse.M195.OfficialAmdCatalogRegistration.v1",
            CreatedUtc = DateTimeOffset.UtcNow,
            Action = action,
            RegistrationMutated = mutated,
            OriginalCoverage = done.OriginalCoverage,
            RegisteredOriginalCoverage = done.RegisteredOriginalCoverage,
            LocalCoverage = done.LocalCoverage,
            RegisteredLocalCoverage = done.RegisteredLocalCoverage,
            DualTrustVerified = done.DualTrustVerified,
            NeverRemovedCatalog = true,
            NeverModifiedCertificates = true,
            DriverBindAttempted = false,
            RebootRequested = false,
            Before = before, After = after
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(receipt, summary, token);
        return new Result(d.RegisteredOriginalCoverage != 14, mutated,
            done.OriginalCoverage == 14, done.RegisteredOriginalCoverage == 14,
            done.DualTrustVerified, action, before, after, receipt);
    }

    private static string AddExactlyOneCatalog(string catalogFile, string selectedBaseName)
    {
        if (selectedBaseName != ManagedName || Path.GetFileName(selectedBaseName) != selectedBaseName)
            throw new InvalidDataException("M195_REGISTRATION_SELECTED_NAME_NOT_PINNED");
        IntPtr admin = IntPtr.Zero;
        IntPtr catalog = IntPtr.Zero;
        try
        {
            Guid subsystem = DriverActionVerify;
            if (!CryptCATAdminAcquireContext2(out admin, ref subsystem,
                "SHA256", IntPtr.Zero, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "M195_REGISTRATION_ACQUIRE_CONTEXT");
            catalog = CryptCATAdminAddCatalog(admin, catalogFile, selectedBaseName, 0);
            if (catalog == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "M195_REGISTRATION_ADD_FAILED");
            var details = new CatalogInfo {
                cbStruct = (uint)Marshal.SizeOf<CatalogInfo>()
            };
            if (!CryptCATCatalogInfoFromContext(catalog, ref details, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "M195_REGISTRATION_RESULT_PATH_FAILED");
            string installed = Path.GetFullPath(details.wszCatalogFile);
            string expectedRoot = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "CatRoot",
                "{" + DriverActionVerify.ToString().ToUpperInvariant() + "}"))
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!installed.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(installed), ManagedName,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("M195_REGISTRATION_RESULT_OUTSIDE_PINNED_CATROOT");
            return installed;
        }
        finally
        {
            if (admin != IntPtr.Zero && catalog != IntPtr.Zero)
                CryptCATAdminReleaseCatalogContext(admin, catalog, 0);
            if (admin != IntPtr.Zero)
                CryptCATAdminReleaseContext(admin, 0);
        }
    }

    public static bool ShouldRegisterOriginal(M195DualCatalogCoverage.Assessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        return assessment.OriginalCatalogRegistrationSuggested;
    }

    private static string Sha(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CatalogInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string wszCatalogFile;
    }
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminAcquireContext2(
        out IntPtr context, ref Guid subsystem,
        [MarshalAs(UnmanagedType.LPWStr)] string hashAlgorithm, IntPtr strongHashPolicy, uint flags);
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CryptCATAdminAddCatalog(
        IntPtr context, [MarshalAs(UnmanagedType.LPWStr)] string catalogPath,
        [MarshalAs(UnmanagedType.LPWStr)] string chosenName, uint flags);
    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATCatalogInfoFromContext(
        IntPtr context, ref CatalogInfo info, uint flags);
    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseCatalogContext(
        IntPtr context, IntPtr catalog, uint flags);
    [DllImport("wintrust.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptCATAdminReleaseContext(IntPtr context, uint flags);
}
