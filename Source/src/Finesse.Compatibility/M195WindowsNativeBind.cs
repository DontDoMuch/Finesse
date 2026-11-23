using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Finesse.Compatibility;

/// <summary>
/// Internal M179/M180 native GPU bind and recovery port. NOT connected to the GUI
/// or to M195WindowsPreparationActions. Caller must own the verified signed
/// stage and explicitly complete original-catalog registration, dual-trust
/// evidence, AMD Software, reboot and postboot separately.
/// </summary>
public static class M195WindowsNativeBind
{
    private const uint ForceUpdate = 1;
    private static readonly Regex HashLine = new(
        @"\A([0-9a-fA-F]{64})  (.+)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    public sealed record BindPlan(string InstanceId, string HardwareId,
        string StagedPublishedInf, string OriginalPublishedInf,
        string OriginalVersion, HandheldProfile Profile);

    internal sealed record BindReceipt(
        BindPlan Plan, bool NativeAccepted, int WindowsError,
        bool RebootRequired, string ObservedPublishedInf,
        string ObservedVersion, uint ObservedProblemCode,
        string EvidencePath);

    /// <summary>
    /// Pure, cross-platform bind identity contract. No PnP mutation.
    /// </summary>
    public static BindPlan RequirePlan(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver original,
        M195WindowsGpuInventory.ExactGpu observed,
        string stagedPublishedInf, string stagedHardwareId,
        string stagedInstanceId, string candidateInfText)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(candidateInfText);
        if (observed.Profile != device.Profile ||
            !string.Equals(observed.Profile.HardwareId, device.ExactHardwareId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observed.Display.InstanceId, device.InstanceId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(stagedInstanceId, device.InstanceId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_BIND_GPU_IDENTITY_DRIFT");
        if (observed.Display.ClassGuid != M195WindowsGpuInventory.DisplayClass ||
            !string.Equals(observed.Display.PublishedInf, original.PublishedInf,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observed.Display.Version, original.Version,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(original.Version))
            throw new InvalidDataException("M195_BIND_BASELINE_ASSIGNMENT_DRIFT");
        var proposal = M195WindowsBindIdentity.InspectProposedBind(
            observed, candidateInfText);
        if (!string.Equals(stagedHardwareId, proposal.HardwareId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_BIND_STAGED_HARDWARE_ID_DRIFT");
        return new BindPlan(device.InstanceId, proposal.HardwareId,
            M195WindowsBindIdentity.RequirePublishedOemInf(stagedPublishedInf),
            M195WindowsBindIdentity.RequirePublishedOemInf(original.PublishedInf),
            original.Version, device.Profile);
    }

    /// <summary>
    /// Exactly one native UpdateDriverForPlugAndPlayDevicesW request, with an
    /// output rebootRequired argument: this API must NOT restart Windows here.
    /// An exact target Code 43 BEFORE reboot is pending, not success.
    /// On any failed/mismatched bind, attempt verified baseline recovery when
    /// possible and record its actual observed Code 0 state.
    /// </summary>
    internal static async Task<BindReceipt> BindAsync(
        M195LiveSigningPreflight.Verified preflight,
        M195LiveCatalogSigner.Signed signed,
        M195WindowsDriverStoreStage.StageReceipt stage,
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline,
        CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native driver binding requires Windows.");
        ArgumentNullException.ThrowIfNull(preflight);
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentNullException.ThrowIfNull(stage);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(baseline);
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent())
                .IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException("M195 binding needs the existing elevated installer process.");
        if (stage.DriverBindAttempted ||
            !string.Equals(stage.SignedCatalogSha256, signed.SignedCatalogSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(stage.SigningThumbprint, signed.SignerThumbprint,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(stage.EvidenceFile) ||
            !File.Exists(preflight.PreparedInf))
            throw new InvalidDataException("M195_BIND_STAGE_RECEIPT_INVALID");
        string evidence = Path.Combine(preflight.EvidenceDirectory,
            "M195-Native-Bind-Receipt.json");
        if (File.Exists(evidence))
            throw new IOException("M195_BIND_ATTEMPT_ALREADY_RECORDED");

        // A Go S / Go 2 test has no permission to bind without the healthy,
        // hashed export. Go 1 retains frozen M194's optional-backup behavior.
        if (device.Profile != HandheldProfiles.Go1 && !baseline.Verified)
            throw new InvalidDataException("M195_BIND_TESTER_REQUIRES_VERIFIED_BACKUP");
        string? backupInf = VerifyRecoveryExport(original, baseline);

        token.ThrowIfCancellationRequested();
        var before = M195WindowsGpuInventory.RequireUniqueExactGpu(
            await M195WindowsGpuInventory.EnumerateAsync(token));
        string candidateInf = await File.ReadAllTextAsync(preflight.PreparedInf, token);
        BindPlan plan = RequirePlan(device, original, before,
            stage.PublishedInf, stage.BindHardwareId, stage.HardwareInstanceId,
            candidateInf);

        // Check the ACTUAL published and DriverStore INF+CAT, not only the
        // preparation folder or a stage API success Boolean.
        _ = M195WindowsDriverStoreLocator.RequireFinesse26111Package(
            stage.PublishedInf, preflight.PreparedInf, signed.CatalogPath,
            signed.SignedCatalogSha256);

        await WriteAsync(evidence, new {
            Schema = "Finesse.M195.NativeBind.v1", Phase = "READY_TO_BIND",
            Plan = plan, BackupVerified = backupInf is not null,
            NativeCallAttempted = false, RestartRequested = false
        });

        // Do not pass NULL for the reboot output parameter. M179 uses an
        // explicit out bool so Windows reports the need without rebooting.
        bool nativeAccepted = UpdateDriverForPlugAndPlayDevicesW(
            IntPtr.Zero, plan.HardwareId, preflight.PreparedInf, ForceUpdate,
            out bool rebootRequired);
        int win32 = nativeAccepted ? 0 : Marshal.GetLastWin32Error();
        M195WindowsGpuInventory.ExactGpu? after = null;
        string? observationError = null;
        if (nativeAccepted)
        {
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    after = M195WindowsGpuInventory.RequireUniqueExactGpu(
                        await M195WindowsGpuInventory.EnumerateAsync(CancellationToken.None));
                    if (IsExpectedAssigned(plan, after)) break;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A native observation failure must not escape after a
                    // mutating bind without attempting recovery and recording
                    // the error. Do not treat a stale prior sample as success.
                    observationError = ex.GetType().Name + ": " + ex.Message;
                    after = null;
                }
                if (i != 5)
                    await Task.Delay(900, CancellationToken.None);
            }
        }
        bool exactAssignment = after is not null && IsExpectedAssigned(plan, after);
        bool pendingHealthyOrCode43 = exactAssignment &&
            after!.Display.ProblemCode is 0 or 43;
        if (!nativeAccepted || !pendingHealthyOrCode43)
        {
            RecoveryEvidence recovery;
            try { recovery = await TryRestoreAsync(plan, backupInf); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                recovery = new(false, 0, false, null, null, null,
                    false, "RECOVERY_EXCEPTION_REQUIRES_REVIEW:" +
                    ex.GetType().Name + ":" + ex.Message);
            }
            await WriteAsync(evidence, new {
                Schema = "Finesse.M195.NativeBind.v1",
                Phase = nativeAccepted ? "BIND_ASSIGNMENT_REJECTED" : "BIND_NATIVE_API_FAILED",
                Plan = plan, NativeCallAttempted = true, NativeAccepted = nativeAccepted,
                WindowsError = win32, RebootRequired = rebootRequired,
                ObservedInf = after?.Display.PublishedInf,
                ObservedVersion = after?.Display.Version,
                ObservedProblem = after?.Display.ProblemCode,
                ObservationError = observationError,
                Recovery = recovery, RestartRequested = false,
                TargetPostbootCode0Verified = false
            });
            if (!nativeAccepted)
                throw new Win32Exception(win32, "Windows rejected the M195 native GPU bind; recovery recorded.");
            throw new InvalidDataException("M195_BIND_ASSIGNED_GPU_NOT_EXPECTED_RECOVERY_RECORDED");
        }

        string phase = after!.Display.ProblemCode == 43
            ? "PREBOOT_CODE43_PENDING_REBOOT_NOT_HEALTHY"
            : "PREBOOT_CODE0_POSTBOOT_NOT_VERIFIED";
        await WriteAsync(evidence, new {
            Schema = "Finesse.M195.NativeBind.v1", Phase = phase,
            Plan = plan, NativeCallAttempted = true, NativeAccepted = true,
            WindowsError = 0, RebootRequired = rebootRequired,
            ObservedInf = after.Display.PublishedInf,
            ObservedVersion = after.Display.Version,
            ObservedProblem = after.Display.ProblemCode,
            RecoveryAttempted = false, RestartRequested = false,
            AmdSoftwareInstalled = false, TargetPostbootCode0Verified = false
        });
        return new BindReceipt(plan, true, 0, rebootRequired,
            after.Display.PublishedInf, after.Display.Version,
            after.Display.ProblemCode, evidence);
    }

    private static bool IsExpectedAssigned(BindPlan plan,
        M195WindowsGpuInventory.ExactGpu observed) =>
        observed.Profile == plan.Profile &&
        string.Equals(observed.Display.InstanceId, plan.InstanceId,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(observed.Display.PublishedInf, plan.StagedPublishedInf,
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(observed.Display.Version,
            OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion,
            StringComparison.Ordinal);

    private static string? VerifyRecoveryExport(
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline)
    {
        if (!baseline.Verified)
        {
            if (baseline.VerifiedExportDirectory is not null)
                throw new InvalidDataException("M195_BIND_UNVERIFIED_BACKUP_DIRECTORY");
            return null;
        }
        string root = Path.GetFullPath(baseline.VerifiedExportDirectory ??
            throw new InvalidDataException("M195_BIND_BACKUP_DIRECTORY_MISSING"));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);
        RejectReparseAncestors(root);
        string manifest = Path.Combine(Path.GetDirectoryName(root) ??
            throw new InvalidDataException("M195_BIND_BACKUP_PARENT_MISSING"), "SHA256SUMS.txt");
        RejectReparseAncestors(manifest);
        if (!File.Exists(manifest))
            throw new InvalidDataException("M195_BIND_BACKUP_MANIFEST_MISSING");
        string liveOriginal = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF",
            M195WindowsBindIdentity.RequirePublishedOemInf(original.PublishedInf));
        RejectReparseAncestors(liveOriginal);
        if (!File.Exists(liveOriginal))
            throw new FileNotFoundException("Original published INF unavailable.", liveOriginal);
        string baselineSha = HashFile(liveOriginal);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchingInf = new List<string>();
        foreach (string line in File.ReadAllLines(manifest))
        {
            var match = HashLine.Match(line);
            if (!match.Success)
                throw new InvalidDataException("M195_BIND_BACKUP_BAD_MANIFEST_LINE");
            string relative = match.Groups[2].Value;
            Amd26111SourceInventory.ValidateRelative(relative);
            if (!expected.Add(relative))
                throw new InvalidDataException("M195_BIND_BACKUP_DUPLICATE_MANIFEST");
            string file = Path.GetFullPath(Path.Combine(root,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!file.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("M195_BIND_BACKUP_PATH_ESCAPE");
            RejectReparseAncestors(file);
            if (!File.Exists(file) || !HashFile(file).Equals(
                    match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("M195_BIND_BACKUP_HASH_MISMATCH");
            if (file.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) &&
                HashFile(file).Equals(baselineSha, StringComparison.OrdinalIgnoreCase))
                matchingInf.Add(file);
        }
        string[] actualFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        if (actualFiles.Length != expected.Count)
            throw new InvalidDataException("M195_BIND_BACKUP_MANIFEST_NOT_COMPLETE");
        if (matchingInf.Count != 1)
            throw new InvalidDataException("M195_BIND_BACKUP_ORIGINAL_INF_NOT_UNIQUE");
        return matchingInf[0];
    }

    internal static async Task<RecoveryEvidence> RestoreAfterStep8FailureAsync(
        BindReceipt bound, M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline)
    {
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(baseline);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Recovery requires Windows.");
        // Reverify every backup file immediately before recovery, rather than
        // trusting the Step-3 flag or the published INF name alone.
        string? backupInf = VerifyRecoveryExport(original, baseline);
        return await TryRestoreAsync(bound.Plan, backupInf);
    }

    /// <summary>
    /// Step 9/10/11 failure recovery before the planned reboot. This is only
    /// invoked after an actual Step8 bound receipt; no alternate driver is
    /// selected and the original export is rehashed before any native call.
    /// The returned receipt is not a success unless actual baseline Code0
    /// is observed and Windows reports no pending restart.
    /// </summary>
    internal static async Task<RecoveryEvidence> RestoreAfterPrebootFailureAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.BoundDriver bound,
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline,
        CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Preboot recovery requires Windows.");
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(bound);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(baseline);
        token.ThrowIfCancellationRequested();
        if (device.Profile != HandheldProfiles.Resolve(device.ExactHardwareId) ||
            !bound.SignedOnce ||
            !string.Equals(bound.TargetDriverVersion,
                OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion,
                StringComparison.Ordinal))
            throw new InvalidDataException("M195_PREBOOT_RECOVERY_TARGET_PROFILE_INVALID");

        // Verify backup contents before asking SetupAPI/NewDev to change any
        // device. No backup is permitted to produce a fabricated Code0.
        string? savedInf = VerifyRecoveryExport(original, baseline);
        var observed = M195WindowsGpuInventory.RequireUniqueExactGpu(
            await M195WindowsGpuInventory.EnumerateAsync(token));
        if (observed.Profile != device.Profile ||
            !string.Equals(observed.Display.InstanceId, device.InstanceId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_PREBOOT_RECOVERY_DEVICE_CHANGED");
        bool targetAssigned = string.Equals(observed.Display.PublishedInf,
            bound.PublishedInf, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observed.Display.Version, bound.TargetDriverVersion,
                StringComparison.Ordinal);
        bool baselineAssigned = string.Equals(observed.Display.PublishedInf,
            original.PublishedInf, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observed.Display.Version, original.Version,
                StringComparison.Ordinal);
        if (!targetAssigned && !baselineAssigned)
            throw new InvalidDataException("M195_PREBOOT_RECOVERY_UNKNOWN_ACTIVE_DRIVER");
        if (baselineAssigned && observed.Display.ProblemCode == 0)
            return new RecoveryEvidence(true, 0, false,
                observed.Display.PublishedInf, observed.Display.Version,
                observed.Display.ProblemCode, true,
                "RECOVERY_VERIFIED_CODE0_BASELINE");
        string? hardwareId = observed.Display.HardwareIds.FirstOrDefault(h =>
            string.Equals(h, device.ExactHardwareId, StringComparison.OrdinalIgnoreCase));
        if (hardwareId is null && device.Profile == HandheldProfiles.Go1)
        {
            string revisionless = device.ExactHardwareId[
                ..device.ExactHardwareId.IndexOf("&REV_", StringComparison.Ordinal)];
            hardwareId = observed.Display.HardwareIds.FirstOrDefault(h =>
                string.Equals(h, revisionless, StringComparison.OrdinalIgnoreCase));
        }
        if (hardwareId is null)
            throw new InvalidDataException("M195_PREBOOT_RECOVERY_EXACT_HARDWARE_ID_MISSING");
        var plan = new BindPlan(device.InstanceId, hardwareId,
            M195WindowsBindIdentity.RequirePublishedOemInf(bound.PublishedInf),
            M195WindowsBindIdentity.RequirePublishedOemInf(original.PublishedInf),
            original.Version, device.Profile);
        return await TryRestoreAsync(plan, savedInf);
    }

    /// <summary>
    /// Recovery after first durable postboot snapshot. The caller must have
    /// proved a genuine new boot, and exactly the physical target display.
    /// Reuses frozen M180 recovery export SHA manifest and active Code 0
    /// observation; it never requests an additional Windows reboot.
    /// </summary>
    internal static Task<RecoveryEvidence> RestoreAfterPostbootFailureAsync(
        M195LifecycleSequence.PendingPostboot pending,
        string observedHardwareId,
        M195WindowsGpuInventory.ExactGpu observed)
    {
        M195PostbootTicketContract.RequireValid(pending);
        ArgumentNullException.ThrowIfNull(observed);
        HandheldProfile expected = HandheldProfiles.Resolve(pending.ExactHardwareId) ??
            throw new InvalidDataException("M195_POSTBOOT_PROFILE_UNKNOWN");
        if (observed.Profile != expected ||
            !observed.Display.InstanceId.Equals(pending.InstanceId,
                StringComparison.OrdinalIgnoreCase) ||
            !observed.Display.HardwareIds.Any(h =>
                h.Equals(observedHardwareId, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("M195_POSTBOOT_RECOVERY_HARDWARE_ID_DRIFT");
        var original = new M195LifecycleSequence.AssignedDriver(
            pending.OriginalPublishedInf, pending.OriginalDriverVersion,
            0, pending.VerifiedBackupDirectory is not null,
            "Original driver from verified preboot transaction");
        var baseline = new M195LifecycleSequence.Baseline(
            pending.VerifiedBackupDirectory,
            pending.VerifiedBackupDirectory is not null,
            pending.VerifiedBackupDirectory is null ? null :
                pending.OriginalPublishedInf);
        string? verifiedInf = VerifyRecoveryExport(original, baseline);
        var plan = new BindPlan(pending.InstanceId, observedHardwareId,
            pending.ExpectedPublishedInf, pending.OriginalPublishedInf,
            pending.OriginalDriverVersion, expected);
        return TryRestoreAsync(plan, verifiedInf);
    }

    internal sealed record RecoveryEvidence(bool ApiAccepted, int WindowsError,
        bool RebootRequired, string? ObservedInf, string? ObservedVersion,
        uint? ObservedProblemCode, bool ConfirmedCode0, string Status);

    private static async Task<RecoveryEvidence> TryRestoreAsync(
        BindPlan plan, string? backupInf)
    {
        if (backupInf is null)
            return new(false, 0, false, null, null, null, false,
                "NO_VERIFIED_RECOVERY_EXPORT");
        bool accepted = UpdateDriverForPlugAndPlayDevicesW(
            IntPtr.Zero, plan.HardwareId, backupInf, ForceUpdate, out bool restart);
        int code = accepted ? 0 : Marshal.GetLastWin32Error();
        M195WindowsGpuInventory.ExactGpu? observed = null;
        for (int i = 0; accepted && i < 6; i++)
        {
            try
            {
                observed = M195WindowsGpuInventory.RequireUniqueExactGpu(
                    await M195WindowsGpuInventory.EnumerateAsync(CancellationToken.None));
                if (observed.Profile == plan.Profile &&
                    string.Equals(observed.Display.InstanceId, plan.InstanceId,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(observed.Display.PublishedInf,
                        plan.OriginalPublishedInf, StringComparison.OrdinalIgnoreCase) &&
                    observed.Display.Version == plan.OriginalVersion &&
                    observed.Display.ProblemCode == 0)
                    break;
            }
            catch (Exception) { /* Preserve the most recent recoverable evidence. */ }
            if (i != 5)
                await Task.Delay(900, CancellationToken.None);
        }
        string status = ClassifyRecovery(accepted, restart, plan, observed);
        bool healthy = status == "RECOVERY_VERIFIED_CODE0_BASELINE";
        return new(accepted, code, restart,
            observed?.Display.PublishedInf, observed?.Display.Version,
            observed?.Display.ProblemCode, healthy, status);
    }

    /// <summary>
    /// Frozen M180 rollback-state evaluator, with a separate pending-restart
    /// disposition. Even exact Code 0 before an OS-required restart does NOT
    /// establish completed recovery.
    /// </summary>
    public static string ClassifyRecovery(
        bool apiAccepted, bool restartRequired, BindPlan plan,
        M195WindowsGpuInventory.ExactGpu? observed)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!apiAccepted)
            return "RECOVERY_BIND_API_FAILED";
        if (observed is null)
            return restartRequired
                ? "RECOVERY_PENDING_RESTART_UNVERIFIED"
                : "RECOVERY_DEVICE_NOT_OBSERVED";
        if (observed.Profile != plan.Profile ||
            !string.Equals(observed.Display.InstanceId, plan.InstanceId,
                StringComparison.OrdinalIgnoreCase))
            return "RECOVERY_DEVICE_ID_MISMATCH";
        if (!string.Equals(observed.Display.PublishedInf,
                plan.OriginalPublishedInf, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observed.Display.Version, plan.OriginalVersion,
                StringComparison.Ordinal))
            return restartRequired
                ? "RECOVERY_PENDING_RESTART_UNVERIFIED"
                : "RECOVERY_BASELINE_MISMATCH";
        if (observed.Display.ProblemCode != 0)
            return restartRequired
                ? "RECOVERY_PENDING_RESTART_UNVERIFIED"
                : "RECOVERY_DEVICE_PROBLEM";
        if (restartRequired)
            return "RECOVERY_PENDING_RESTART_UNVERIFIED";
        return "RECOVERY_VERIFIED_CODE0_BASELINE";
    }

    private static async Task WriteAsync(string path, object receipt)
    {
        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp,
            JsonSerializer.Serialize(receipt,
                new JsonSerializerOptions { WriteIndented = true }),
            CancellationToken.None);
        File.Move(temp, path, true);
    }

    private static string HashFile(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RejectReparseAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_BIND_REPARSE_PATH");
            string? parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent;
        }
    }

    [DllImport("newdev.dll", EntryPoint = "UpdateDriverForPlugAndPlayDevicesW",
        CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateDriverForPlugAndPlayDevicesW(
        IntPtr parent, [MarshalAs(UnmanagedType.LPWStr)] string hardwareId,
        [MarshalAs(UnmanagedType.LPWStr)] string fullInfPath, uint flags,
        [MarshalAs(UnmanagedType.Bool)] out bool rebootRequired);
}
