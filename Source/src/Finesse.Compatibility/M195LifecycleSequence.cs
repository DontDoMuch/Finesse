namespace Finesse.Compatibility;

/// <summary>
/// M195 source-first lifecycle coordinator extracted from the actual frozen
/// M182InstallLifecycleWorkflow ordering. This class performs no native Windows
/// operations itself: only concrete Windows implementations of the interface
/// may perform signing, staging, binding or reboot.
/// </summary>
public static class M195LifecycleSequence
{
    public enum Step
    {
        IdentifyDevice = 1,
        InspectDriver = 2,
        BackupDriver = 3,
        AcquireAmdDriver = 4,
        ExtractAmdDriver = 5,
        PrepareDeviceDriver = 6,
        GenerateCatalog = 7,
        SignStageBindAndValidate = 8,
        VerifyPreboot = 9,
        InstallAdrenalin = 10,
        RegisterAndRequestRestart = 11,
        VerifyPostboot = 12
    }

    public enum PrebootState { Code0AwaitingPostboot, Code43AwaitingPostboot, Reject }
    public enum PostbootState { TargetConfirmedCode0, TargetFailedRecovered, TargetFailedRecoveryUnverified }

    public sealed record Device(string InstanceId, string ExactHardwareId, HandheldProfile Profile);
    public sealed record AssignedDriver(string PublishedInf, string Version, int ProblemCode,
        bool BackupEligible, string BackupDecision);
    public sealed record Baseline(string? VerifiedExportDirectory, bool Verified, string? RecoveryInf);
    public sealed record GeneratedCatalog(string UnsignedCatalogPath, string Sha256,
        int VerifiedDriverFiles, bool CatalogContainsSigners);
    public sealed record BoundDriver(string PublishedInf, string TargetDriverVersion,
        string MainSignedCatalogSha256, int DualTrustedCriticalFiles,
        bool OriginalAmdCatalogVerified, bool SignedOnce, bool RebootRequired);
    public sealed record PrebootObservation(string InstanceId, string PublishedInf,
        string DriverVersion, int ProblemCode, PrebootState Classification);
    public sealed record SoftwareResult(bool InstalledAndVerified, string Status, int MsiExitCode);
    public sealed record RegisteredPostboot(string TransactionId, bool RegistrationVerified);
    public sealed record PostbootObservation(string InstanceId, string PublishedInf,
        string DriverVersion, int ProblemCode, bool SoftwareInstalled, string BootIdentity);
    public sealed record RecoveryResult(bool Restored, string? RestoredInf, int? ProblemCode);

    public sealed record PendingPostboot(
        string TransactionId, string InstanceId, string ExactHardwareId,
        string ExpectedPublishedInf, string ExpectedDriverVersion,
        string ExpectedInstallerSha256, string ExpectedMainCatalogSha256,
        string OriginalPublishedInf, string OriginalDriverVersion,
        string? VerifiedBackupDirectory, bool PrebootCode43, string RegisteredBootIdentity);

    public sealed record PostbootOutcome(PostbootState Status, PostbootObservation FirstObservation,
        RecoveryResult? Recovery, bool TargetDriverConfirmed);

    public interface IWindowsInstallationActions
    {
        Task<Device> IdentifyExactDeviceAsync(CancellationToken token);
        Task<AssignedDriver> InspectAssignedDriverAsync(Device device, CancellationToken token);
        Task<Baseline> ExportBaselineAsync(Device device, AssignedDriver assigned, CancellationToken token);
        Task<OfficialAmdInstallerVerifier.VerifiedSource> AcquireVerifiedAmdInstallerAsync(
            CancellationToken token);
        Task<Amd26111ArchiveExtraction.VerifiedExtraction> ExtractOriginalAmdAsync(
            OfficialAmdInstallerVerifier.VerifiedSource source, CancellationToken token);
        Task<Amd26111TwoPhasePreparation.PreparationReceipt> BuildUnsignedCandidateAsync(
            Device device, OfficialAmdInstallerVerifier.VerifiedSource source,
            Amd26111ArchiveExtraction.VerifiedExtraction extracted, CancellationToken token);
        Task<GeneratedCatalog> GenerateUnsignedCatalogAsync(
            Amd26111TwoPhasePreparation.PreparationReceipt prepared, CancellationToken token);
        Task<BoundDriver> SignOnceStageBindAndVerifyDualTrustAsync(
            Device device, Amd26111TwoPhasePreparation.PreparationReceipt prepared,
            GeneratedCatalog generated, Baseline baseline, CancellationToken token);
        Task<PrebootObservation> ObservePrebootAsync(
            Device device, BoundDriver bound, CancellationToken token);
        Task<SoftwareResult> InstallVerifiedAmdSoftwareAsync(
            OfficialAmdInstallerVerifier.VerifiedSource source, BoundDriver bound, CancellationToken token);
        Task<RegisteredPostboot> RegisterPostbootAsync(
            PendingPostboot pending, AssignedDriver original, Baseline baseline,
            CancellationToken token);
        Task<string> ReadPreRebootBootIdentityAsync(CancellationToken token);
        Task RequestSingleRestartAsync(RegisteredPostboot registration, CancellationToken token);
        Task<RecoveryResult> RestoreBaselineAfterPrebootFailureAsync(
            Device device, AssignedDriver original, Baseline baseline, CancellationToken token);
    }

    public interface IPostbootActions
    {
        /// <summary>
        /// Absolutely the first device-state operation after the real boot
        /// transition: do NOT recover or rebind before this receipt exists.
        /// </summary>
        Task<PostbootObservation> CaptureFirstObservationAsync(
            PendingPostboot pending, CancellationToken token);
        Task<RecoveryResult> RecoverBaselineAsync(
            PendingPostboot pending, PostbootObservation first, CancellationToken token);
    }

    public static async Task<PendingPostboot> RunPrebootAsync(
        IWindowsInstallationActions actions,
        Action<Step, string>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        Device? device = null;
        AssignedDriver? assigned = null;
        Baseline? baseline = null;
        BoundDriver? bound = null;
        bool restartRequested = false;
        int priorStep = 0;

        void Advance(Step step, string message)
        {
            if ((int)step != priorStep + 1)
                throw new InvalidOperationException("M195_LIFECYCLE_STEP_ORDER_INVALID");
            priorStep = (int)step;
            progress?.Invoke(step, message);
        }

        try
        {
            Advance(Step.IdentifyDevice, "Checking your device");
            device = await actions.IdentifyExactDeviceAsync(token);
            if (device is null || device.Profile is null ||
                device.Profile != HandheldProfiles.Resolve(device.ExactHardwareId) ||
                string.IsNullOrWhiteSpace(device.InstanceId))
                throw new InvalidDataException("M195_LIFECYCLE_EXACT_DEVICE_REQUIRED");

            Advance(Step.InspectDriver, "Inspecting current graphics driver");
            assigned = await actions.InspectAssignedDriverAsync(device, token);
            if (assigned is null || string.IsNullOrWhiteSpace(assigned.PublishedInf) ||
                string.IsNullOrWhiteSpace(assigned.Version))
                throw new InvalidDataException("M195_LIFECYCLE_ORIGINAL_DRIVER_IDENTITY_MISSING");

            Advance(Step.BackupDriver, "Preserving the current driver");
            baseline = await actions.ExportBaselineAsync(device, assigned, token);
            if (baseline is null ||
                (device.Profile != HandheldProfiles.Go1 &&
                    (!baseline.Verified || string.IsNullOrWhiteSpace(baseline.VerifiedExportDirectory))))
                throw new InvalidDataException("M195_LIFECYCLE_TESTER_RECOVERY_BACKUP_REQUIRED");

            Advance(Step.AcquireAmdDriver, "Verifying the official AMD installer");
            var source = await actions.AcquireVerifiedAmdInstallerAsync(token);
            if (source is null ||
                source.Sha256 != OfficialAmd26111SourceContract.VerifiedFrozenM194.InstallerSha256 ||
                source.Bytes != OfficialAmd26111SourceContract.VerifiedFrozenM194.ExactInstallerBytes ||
                string.IsNullOrWhiteSpace(source.SignerThumbprint))
                throw new InvalidDataException("M195_LIFECYCLE_AMD_SOURCE_RECEIPT_MISMATCH");

            Advance(Step.ExtractAmdDriver, "Checking the original AMD graphics files");
            var extracted = await actions.ExtractOriginalAmdAsync(source, token);
            if (extracted is null || extracted.OriginalFilesVerified != 194 ||
                extracted.SourceManifestSha256 != Amd26111SourceInventory.PinnedOriginalManifestSha256 ||
                string.IsNullOrWhiteSpace(extracted.OriginalCatalogSha256))
                throw new InvalidDataException("M195_LIFECYCLE_ORIGINAL_194_FILES_UNVERIFIED");

            Advance(Step.PrepareDeviceDriver, "Preparing your graphics-driver profile");
            var prepared = await actions.BuildUnsignedCandidateAsync(
                device, source, extracted, token);
            if (prepared is null ||
                prepared.Candidate.Profile != device.Profile ||
                prepared.UnsignedStage.VerifiedFiles != 194 ||
                prepared.UnsignedStage.SignedOrInstallable ||
                prepared.UnsignedStage.CandidateInventorySha256 !=
                    Amd26111SourceInventory.PinnedCandidateManifestHash(device.Profile) ||
                prepared.Installer.Sha256 != source.Sha256)
                throw new InvalidDataException("M195_LIFECYCLE_UNSIGNED_CANDIDATE_IDENTITY_FAILED");

            Advance(Step.GenerateCatalog, "Generating the new driver catalog");
            var generated = await actions.GenerateUnsignedCatalogAsync(prepared, token);
            if (generated is null || generated.VerifiedDriverFiles != 194 ||
                generated.CatalogContainsSigners ||
                string.IsNullOrWhiteSpace(generated.Sha256))
                throw new InvalidDataException("M195_LIFECYCLE_UNSIGNED_CATALOG_MISSING");

            Advance(Step.SignStageBindAndValidate, "Installing and validating the signed driver");
            bound = await actions.SignOnceStageBindAndVerifyDualTrustAsync(
                device, prepared, generated, baseline, token);
            // M194 M191 captured 14x4 dual-trust evidence independently.
            // Incomplete or unavailable OPTIONAL catalog audit is recorded,
            // never upgraded to "14/14" and never used as an invented
            // unconditional installation gate. The native signed/staged/assigned
            // target identity is still mandatory.
            if (bound is null || !bound.SignedOnce ||
                bound.DualTrustedCriticalFiles is < 0 or > 14 ||
                bound.TargetDriverVersion !=
                    OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion ||
                string.IsNullOrWhiteSpace(bound.PublishedInf) ||
                string.IsNullOrWhiteSpace(bound.MainSignedCatalogSha256))
                throw new InvalidDataException("M195_LIFECYCLE_SIGN_BIND_CORE_IDENTITY_INCOMPLETE");

            Advance(Step.VerifyPreboot, "Checking your GPU before restart");
            var preboot = await actions.ObservePrebootAsync(device, bound, token);
            if (preboot is null ||
                !string.Equals(preboot.InstanceId, device.InstanceId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(preboot.PublishedInf, bound.PublishedInf, StringComparison.OrdinalIgnoreCase) ||
                preboot.DriverVersion != bound.TargetDriverVersion ||
                preboot.Classification == PrebootState.Reject ||
                (preboot.ProblemCode == 43 && preboot.Classification != PrebootState.Code43AwaitingPostboot) ||
                (preboot.ProblemCode == 0 && preboot.Classification != PrebootState.Code0AwaitingPostboot) ||
                preboot.ProblemCode is not (0 or 43))
                throw new InvalidDataException("M195_LIFECYCLE_PREBOOT_GPU_IDENTITY_REJECTED");

            // M194 invariant: AMD Software MUST be verified before any request
            // to register/schedule a Windows reboot. Provisional Code 43 is
            // explicitly NOT a healthy postboot result.
            Advance(Step.InstallAdrenalin, "Installing AMD Software");
            var software = await actions.InstallVerifiedAmdSoftwareAsync(source, bound, token);
            if (software is null || !software.InstalledAndVerified)
                throw new InvalidDataException("M195_LIFECYCLE_AMD_SOFTWARE_NOT_INSTALLED");

            Advance(Step.RegisterAndRequestRestart, "Preparing one restart and postboot verification");
            string bootIdentity = await actions.ReadPreRebootBootIdentityAsync(token);
            if (string.IsNullOrWhiteSpace(bootIdentity))
                throw new InvalidDataException("M195_LIFECYCLE_BOOT_IDENTITY_MISSING");
            // Persist exactly the ticket which the future postboot process
            // will read. Registration MUST not rebuild it from incomplete inputs.
            var pending = new PendingPostboot(Guid.NewGuid().ToString("N"),
                device.InstanceId, device.ExactHardwareId,
                bound.PublishedInf, bound.TargetDriverVersion,
                source.Sha256, bound.MainSignedCatalogSha256,
                assigned.PublishedInf, assigned.Version, baseline.VerifiedExportDirectory,
                preboot.Classification == PrebootState.Code43AwaitingPostboot, bootIdentity);
            var registration = await actions.RegisterPostbootAsync(
                pending, assigned, baseline, token);
            if (registration is null || !registration.RegistrationVerified ||
                !string.Equals(registration.TransactionId, pending.TransactionId,
                    StringComparison.Ordinal))
                throw new InvalidDataException("M195_LIFECYCLE_POSTBOOT_REGISTRATION_FAILED");

            // Exactly one normal reboot request. The adapter must persist
            // "pending" transaction data as part of RegisterPostbootAsync.
            restartRequested = true;
            await actions.RequestSingleRestartAsync(registration, token);
            return pending;
        }
        catch
        {
            // Mirror M182's preboot recovery behavior ONLY after a real bind
            // was reported, and never trigger another automatic restart.
            if (bound is not null && device is not null && assigned is not null &&
                baseline is not null && !restartRequested)
            {
                try
                {
                    await actions.RestoreBaselineAfterPrebootFailureAsync(
                        device, assigned, baseline, CancellationToken.None);
                }
                catch
                {
                    // Actual live adapter logs separate rollback evidence.
                    // Never hide the original lifecycle failure.
                }
            }
            throw;
        }
    }

    public static async Task<PostbootOutcome> RunPostbootAsync(
        PendingPostboot pending, IPostbootActions actions,
        Action<Step, string>? progress = null,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(actions);
        progress?.Invoke(Step.VerifyPostboot, "Verifying the GPU after restart");

        // Observation MUST happen before any recovery or further driver mutation.
        var first = await actions.CaptureFirstObservationAsync(pending, token);
        if (first is null || string.IsNullOrWhiteSpace(first.BootIdentity) ||
            first.BootIdentity == pending.RegisteredBootIdentity)
            throw new InvalidDataException("M195_LIFECYCLE_REAL_REBOOT_NOT_CONFIRMED");

        bool target = first.ProblemCode == 0 && first.SoftwareInstalled &&
            string.Equals(first.InstanceId, pending.InstanceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(first.PublishedInf, pending.ExpectedPublishedInf,
                StringComparison.OrdinalIgnoreCase) &&
            first.DriverVersion == pending.ExpectedDriverVersion;

        if (target)
            return new PostbootOutcome(PostbootState.TargetConfirmedCode0, first, null, true);

        // A recovered baseline is NOT a successful installation of the target.
        var recovered = await actions.RecoverBaselineAsync(pending, first, token);
        return new PostbootOutcome(
            recovered is { Restored: true, ProblemCode: 0 }
                ? PostbootState.TargetFailedRecovered
                : PostbootState.TargetFailedRecoveryUnverified,
            first, recovered, false);
    }
}
