using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Real native M179 driver-store STAGING only (SetupCopyOEMInfW) for an exact
/// previously signed M195 candidate. This does not bind the GPU and does not
/// claim 14/14 dual-catalog trust or postboot success.
/// The live GUI must not call it until binding, catalog registration and
/// recovery are integrated into the same transaction.
/// </summary>
internal static class M195WindowsDriverStoreStage
{
    internal sealed record StageReceipt(
        string PublishedInf, string SignedCatalogSha256,
        string BindHardwareId, string HardwareInstanceId,
        string SigningThumbprint, string EvidenceFile,
        bool DriverBindAttempted, bool OriginalCatalogRegistered);

    internal static async Task<StageReceipt> StageOnlyAsync(
        M195LiveSigningPreflight.Verified preflight,
        M195LiveCatalogSigner.Signed signed,
        M195LifecycleSequence.Device expectedDevice,
        M195LifecycleSequence.Baseline recovery,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(expectedDevice);
        ArgumentNullException.ThrowIfNull(recovery);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native staging requires Windows.");

        // Go S and Go 2 require an actual verified baseline export before any
        // machine mutation. Never substitute the current OEM INF name for it.
        if (expectedDevice.Profile != HandheldProfiles.Go1 &&
            (!recovery.Verified ||
             string.IsNullOrWhiteSpace(recovery.VerifiedExportDirectory) ||
             !Directory.Exists(recovery.VerifiedExportDirectory)))
            throw new InvalidDataException("M195_STAGE_TESTER_BASELINE_MISSING");

        if (!M195LiveSigningPreflight.IsOwnedSigner(
                signed.SignerThumbprint, signed.SignerSubject) ||
            !signed.TrustRetained ||
            signed.SignedCatalogSha256 == preflight.UnsignedCatalogSha256 ||
            !string.Equals(Path.GetFullPath(signed.CatalogPath),
                Path.GetFullPath(preflight.UnsignedMainCatalog),
                StringComparison.OrdinalIgnoreCase) ||
            HashFile(signed.CatalogPath) != signed.SignedCatalogSha256 ||
            HashFile(preflight.OriginalAmdCatalog) !=
                M195LiveSigningPreflight.PinnedOriginalAmdMainCatalogSha256)
            throw new InvalidDataException("M195_STAGE_SIGNER_OR_CATALOG_NOT_VERIFIED");

        // Re-identify the unique present device immediately before native
        // staging; never trust an earlier device selection alone.
        var devices = await M195WindowsGpuInventory.EnumerateAsync(token);
        var observed = M195WindowsGpuInventory.RequireUniqueExactGpu(devices);
        if (!string.Equals(observed.Display.InstanceId, expectedDevice.InstanceId,
                StringComparison.OrdinalIgnoreCase) ||
            observed.Profile != expectedDevice.Profile ||
            !string.Equals(observed.Profile.HardwareId,
                expectedDevice.ExactHardwareId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_STAGE_DEVICE_CHANGED");

        string infContents = await File.ReadAllTextAsync(preflight.PreparedInf, token);
        M195WindowsBindIdentity.ProposedBind proposed =
            M195WindowsBindIdentity.InspectProposedBind(observed, infContents);
        if (proposed.Profile != expectedDevice.Profile ||
            proposed.InstanceId != expectedDevice.InstanceId)
            throw new InvalidDataException("M195_STAGE_BIND_PREFLIGHT_MISMATCH");

        string evidence = Path.Combine(preflight.EvidenceDirectory,
            "M195-Native-StageOnly-Receipt.json");
        if (File.Exists(evidence))
            throw new IOException("M195_STAGE_ALREADY_ATTEMPTED");
        var buffer = new StringBuilder(1024);
        bool staged = SetupCopyOEMInfW(preflight.PreparedInf, IntPtr.Zero, 0, 0,
            buffer, checked((uint)buffer.Capacity), out uint required, IntPtr.Zero);
        int win32 = staged ? 0 : Marshal.GetLastWin32Error();
        if (!staged)
        {
            // Frozen M179 expressly removes only the new unused signer after
            // SetupCopyOEMInf reports staging failure. Cleanup failures are
            // recorded separately, never silently swallowed.
            string cleanup = "not attempted";
            try
            {
                M195WindowsSignerCleanup.RemoveUnstagedSigner(signed,
                    stageApiReportedSuccess: false);
                cleanup = "scoped owned signer removed";
            }
            catch (Exception ex)
            {
                cleanup = "review required: " + ex.GetType().Name + " " + ex.Message;
            }
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new {
                Schema = "Finesse.M195.NativeDriverStoreStage.v1",
                Phase = "STAGE_API_FAILED",
                WindowsError = win32,
                SignerCleanup = cleanup,
                HardwareId = proposed.HardwareId,
                DriverBindAttempted = false
            }, new JsonSerializerOptions { WriteIndented = true }),
                token);
            throw new Win32Exception(win32,
                "SetupCopyOEMInf rejected the prepared signed package.");
        }

        // Once Windows stages the signed driver package, its trust must be
        // retained. An ambiguous INF/hash after success requires investigation,
        // NOT removal of the signer or a blind second staging call.
        string published = M195WindowsBindIdentity.RequirePublishedOemInf(
            Path.GetFileName(buffer.ToString()));
        string stagedInf = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "INF", published);
        if (!File.Exists(stagedInf) ||
            HashFile(stagedInf) != HashFile(preflight.PreparedInf))
            throw new InvalidDataException(
                "M195_STAGE_PUBLISHED_INF_HASH_NOT_EQUAL_AFTER_SUCCESS");
        await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new {
            Schema = "Finesse.M195.NativeDriverStoreStage.v1",
            Phase = "STAGED_NOT_BOUND",
            PublishedInf = published,
            StagingWin32 = win32,
            WindowsRequiredBufferChars = required,
            ExactHardwareId = proposed.HardwareId,
            InstanceId = proposed.InstanceId,
            MainSignedCatalogSha256 = signed.SignedCatalogSha256,
            SignerThumbprint = signed.SignerThumbprint,
            SignerTrustRetained = true,
            OriginalAmdCatalogRegistered = false,
            DualCatalogCoverageValidated = false,
            DriverBindAttempted = false,
            RebootRequested = false
        }, new JsonSerializerOptions { WriteIndented = true }),
            token);
        return new StageReceipt(published, signed.SignedCatalogSha256,
            proposed.HardwareId, observed.Display.InstanceId,
            signed.SignerThumbprint, evidence, false, false);
    }

    private static string HashFile(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    [DllImport("setupapi.dll", EntryPoint = "SetupCopyOEMInfW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupCopyOEMInfW(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceInf,
        IntPtr mediaLocation, uint mediaType, uint copyStyle,
        StringBuilder destinationInf, uint destinationChars,
        out uint requiredChars, IntPtr component);
}
