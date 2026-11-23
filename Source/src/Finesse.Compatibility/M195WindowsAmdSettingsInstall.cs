using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Frozen M140/M184 Step 10 native AMD Settings MSI transaction (internal).
/// The target driver must ALREADY be assigned. Does not sign, stage or bind a
/// GPU, reboot Windows, install VC++, or execute the AMD full installer.
/// This is disconnected from the GUI until Steps 11-12 are complete.
/// </summary>
internal static class M195WindowsAmdSettingsInstall
{
    internal sealed record Attempt(int Number, string Operation,
        string Target, string LogFile, string? LogSha256,
        int ExitCode, bool RebootRequired);
    internal sealed record Outcome(bool InstalledAndVerified, string Status,
        int? ExitCode, bool PendingReboot, string EvidenceFile,
        IReadOnlyList<Attempt> Attempts, string StoreCleanupStatus,
        string ExactPublishedInf);

    internal static async Task<Outcome> ExecuteAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.BoundDriver assigned,
        M195AmdSettingsMsiPreparation.Prepared prepared,
        string freshEvidenceRoot,
        CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AMD Settings MSI requires Windows.");
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(assigned);
        ArgumentNullException.ThrowIfNull(prepared);
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("M195 requires the existing single elevated installer.");
        }

        // Recheck byte identity immediately before native Windows Installer
        // operations. The actual MSI property table is also inspected again.
        string msi = Path.GetFullPath(prepared.MsiPath);
        if (!File.Exists(msi) ||
            new FileInfo(msi).Length != M195AmdSettings26111.MsiBytes ||
            Hash(msi) != M195AmdSettings26111.MsiSha256 ||
            prepared.MsiSha256 != M195AmdSettings26111.MsiSha256)
            throw new InvalidDataException("M195_M140_MSI_CHANGED_AFTER_EXTRACTION");
        var installedIdentity = M195WindowsAmdSettingsMsiIdentity.ReadExactMsiProperties(msi);
        M195WindowsAmdSettingsMsiIdentity.RequirePinned26111(installedIdentity);
        if (installedIdentity != prepared.Identity)
            throw new InvalidDataException("M195_M140_MSI_IDENTITY_CHANGED");

        string root = Path.GetFullPath(freshEvidenceRoot);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("M195_M140_FRESH_MSI_TRANSACTION_REQUIRED");
        string parent = Path.GetDirectoryName(root) ??
            throw new InvalidDataException("M195_M140_MSI_EVIDENCE_PARENT_MISSING");
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException(parent);
        Directory.CreateDirectory(root);
        string evidence = Path.Combine(root, "M195-AMDSettings-Installation.json");
        var attempts = new List<Attempt>();
        string storeStatus = "STORE_NOT_RUN";
        string originalInf = M195WindowsBindIdentity.RequirePublishedOemInf(assigned.PublishedInf);
        bool enteredMsi = false;
        string state = "PREPARED_NOT_STARTED";
        int? finalExit = null;
        bool restartPending = false;

        async Task PersistAsync(string status, string? error = null)
        {
            string scratch = evidence + ".tmp";
            var data = new {
                Schema = "Finesse.M195.AMDSettingsInstallation.v1",
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                Status = status, Error = error, TargetDriver = assigned.TargetDriverVersion,
                DeviceHardwareId = device.ExactHardwareId,
                DeviceInstanceId = device.InstanceId,
                AssignedInf = originalInf,
                OriginalAmdSoftwareSha256 = prepared.MsiSha256,
                ExtractionEvidence = prepared.ExtractionEvidence,
                StoreCleanup = storeStatus,
                MSIStarted = enteredMsi, Attempts = attempts,
                FinalMsiExit = finalExit, RebootPending = restartPending,
                RestartRequested = false, DriverBindingAttempted = false,
                VCPlusPlusRedistributableChanged = false,
                PostbootCode0Verified = false
            };
            await File.WriteAllTextAsync(scratch,
                JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), CancellationToken.None);
            File.Move(scratch, evidence, true);
        }

        async Task<M195LifecycleSequence.PrebootObservation> RequireGpuAsync()
        {
            var observation = await M195WindowsPrebootObservation.ObserveAsync(
                device, assigned, CancellationToken.None);
            if (observation.Classification == M195LifecycleSequence.PrebootState.Reject)
                throw new InvalidDataException("M195_M140_GPU_ASSIGNMENT_CHANGED_DURING_SOFTWARE");
            return observation;
        }

        try
        {
            _ = await RequireGpuAsync();
            await PersistAsync(state);
            var store = await M195WindowsStoreAmdSettingsCleanup.RemoveCurrentUserAsync(
                root, token);
            storeStatus = store.Status;
            _ = await RequireGpuAsync();
            state = "STORE_CHECK_COMPLETE";
            await PersistAsync(state);

            string native = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                M195AmdSettings26111.NativeFrontendRelative.Replace('/',
                    Path.DirectorySeparatorChar));
            if (M195WindowsAmdSettingsMsiIdentity.ExactTargetInstalled() &&
                File.Exists(native))
            {
                state = "ALREADY_INSTALLED_TARGET_MSI_AND_FRONTEND";
                await PersistAsync(state);
                return new Outcome(true, state, null, false,
                    evidence, attempts.AsReadOnly(), storeStatus, originalInf);
            }

            string msiexec = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "msiexec.exe");
            if (!File.Exists(msiexec))
                throw new FileNotFoundException("Windows Installer system executable missing.", msiexec);
            async Task<Attempt> RunOnceAsync(string operation, string target,
                int ordinal, bool isInstall, TimeSpan maximum)
            {
                // Native Windows Installer's /qn prevents UI and /norestart
                // defers any requested reboot until AFTER software verification.
                string log = Path.Combine(root,
                    "M195-MSI-" + ordinal.ToString("D2") + "-" +
                    Guid.NewGuid().ToString("N") + ".log");
                if (File.Exists(log))
                    throw new IOException("M195_MSI_LOG_ALREADY_PRESENT");
                var start = new ProcessStartInfo(msiexec) {
                    UseShellExecute = false, CreateNoWindow = true
                };
                foreach (string a in M195AmdSettings26111.BuildMsiexecArguments(
                    isInstall, target, log))
                    start.ArgumentList.Add(a);
                enteredMsi = true;
                state = operation + "_STARTED";
                await PersistAsync(state);
                using Process process = Process.Start(start) ??
                    throw new IOException("M195_WINDOWS_INSTALLER_DID_NOT_START");
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
                limit.CancelAfter(maximum);
                try
                {
                    await process.WaitForExitAsync(limit.Token);
                }
                catch (OperationCanceledException)
                {
                    // NEVER kill a running Windows Installer or immediately retry:
                    // completion state is unknown, regardless of timeout/cancel.
                    state = operation + "_RESULT_UNKNOWN_MSI_STILL_MAY_BE_RUNNING";
                    await PersistAsync(state, "MSI_PID=" + process.Id);
                    throw new TimeoutException("M195 MSI still running or cancelled; do not retry or reboot.");
                }
                string? logSha = File.Exists(log) ? Hash(log) : null;
                var attempt = new Attempt(ordinal, operation, target, log,
                    logSha, process.ExitCode, process.ExitCode == 3010);
                attempts.Add(attempt);
                finalExit = attempt.ExitCode;
                restartPending |= attempt.RebootRequired;
                state = operation + "_COMPLETED";
                await PersistAsync(state);
                return attempt;
            }

            // Frozen M184 correction: install target normally FIRST.
            // Do NOT pre-remove based on fragile MSI version comparison.
            Attempt first = await RunOnceAsync("INSTALL_FIRST", msi, 1, true,
                TimeSpan.FromMinutes(30));
            Attempt latest = first;

            if (first.ExitCode == 1603 && File.Exists(first.LogFile) &&
                new FileInfo(first.LogFile).Length <= 8_000_000)
            {
                string log = await File.ReadAllTextAsync(first.LogFile, token);
                string[] blockers =
                    M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(
                        first.ExitCode, log).ToArray();
                if (blockers.Length > 0)
                {
                    var registered =
                        M195WindowsAmdSettingsMsiIdentity.EnumerateRelatedDesktopProducts();
                    IReadOnlyList<M195AmdSettings26111.RegisteredProduct> exact =
                        M195AmdSettings26111.RequireVerifiedBlockers(blockers, registered);
                    _ = await RequireGpuAsync();
                    int index = 2;
                    foreach (var product in exact)
                    {
                        token.ThrowIfCancellationRequested();
                        Attempt removal = await RunOnceAsync(
                            "REMOVE_VERIFIED_WIX_BLOCKER", product.Code,
                            index++, false, TimeSpan.FromMinutes(20));
                        if (!M195AmdSettings26111.IsTargetMsiSuccess(removal.ExitCode) ||
                            M195WindowsAmdSettingsMsiIdentity
                                .EnumerateRelatedDesktopProducts().Any(item =>
                                    item.Code.Equals(product.Code,
                                        StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidDataException(
                                "M195_M184_VERIFIED_BLOCKER_UNINSTALL_NOT_CONFIRMED");
                        _ = await RequireGpuAsync();
                    }
                    // Re-check exact MSI package bytes and target GPU identity
                    // before the single permitted retry, not before every
                    // arbitrarily high version-number comparison.
                    if (Hash(msi) != M195AmdSettings26111.MsiSha256)
                        throw new InvalidDataException("M195_M184_MSI_CHANGED_BEFORE_RETRY");
                    _ = await RequireGpuAsync();
                    latest = await RunOnceAsync("INSTALL_RETRY_ONCE",
                        msi, index, true, TimeSpan.FromMinutes(30));
                }
            }

            _ = await RequireGpuAsync();
            bool nativeExists = File.Exists(native);
            bool targetProductInstalled =
                M195WindowsAmdSettingsMsiIdentity.ExactTargetInstalled();
            bool verified = M195AmdSettings26111.IsTargetMsiSuccess(latest.ExitCode) &&
                latest.LogSha256 is not null &&
                nativeExists && targetProductInstalled;
            state = verified
                ? (restartPending ? "AMD_SETTINGS_INSTALLED_RESTART_DEFERRED" :
                    "AMD_SETTINGS_INSTALLED_PREBOOT_VERIFIED")
                : "AMD_SETTINGS_NOT_VERIFIED_PRESERVE_MSI_LOGS";
            await PersistAsync(state);
            return new Outcome(verified, state, latest.ExitCode,
                restartPending, evidence, attempts.AsReadOnly(),
                storeStatus, originalInf);
        }
        catch (Exception error)
        {
            try
            {
                await PersistAsync("FAILED_OR_INCOMPLETE_MANUAL_REVIEW",
                    error.GetType().Name + ": " + error.Message);
            }
            catch
            {
                // Preserve original failure; existing MSI logs remain on disk.
            }
            throw;
        }
    }

    private static string Hash(string file)
    {
        using FileStream input = new(file, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
