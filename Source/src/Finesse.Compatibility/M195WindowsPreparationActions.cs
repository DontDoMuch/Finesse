namespace Finesse.Compatibility;

/// <summary>
/// Concrete Windows implementation of lifecycle Steps 1-7 against the
/// REAL native APIs and the source-verified 26.11.1 preparation components.
/// Live Steps 8, 10-12 deliberately remain unimplemented while M194 M175/M179/MSI/
/// postboot functionality is being migrated and verified. It CANNOT install,
/// bind, sign, reboot, or alter trust by itself.
/// </summary>
public sealed class M195WindowsPreparationActions :
    M195LifecycleSequence.IWindowsInstallationActions
{
    private readonly Settings _settings;
    private M195WindowsGpuInventory.ExactGpu? _observed;

    public sealed record Settings(
        string FreshRecoveryRoot,
        string FreshOfficialDownloadRoot,
        string FreshOriginalExtractionRoot,
        string FreshUnsignedCandidateRoot,
        string Pinned7zrExePath,
        string PinnedInf2CatExePath,
        string Original194ManifestText,
        string AuthorizedProfilePolicyRows,
        string? LocalOfficialAmdInstaller = null);

    public sealed record PreparedUnsignedReceipt(
        M195LifecycleSequence.Device Device,
        M195LifecycleSequence.AssignedDriver Assigned,
        M195LifecycleSequence.Baseline Baseline,
        OfficialAmdInstallerVerifier.VerifiedSource VerifiedInstaller,
        Amd26111ArchiveExtraction.VerifiedExtraction Source,
        Amd26111TwoPhasePreparation.PreparationReceipt Candidate,
        M195LifecycleSequence.GeneratedCatalog UnsignedCatalog);

    public M195WindowsPreparationActions(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.FreshRecoveryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.FreshOfficialDownloadRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.FreshOriginalExtractionRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.FreshUnsignedCandidateRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Pinned7zrExePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.PinnedInf2CatExePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.AuthorizedProfilePolicyRows);
        _ = Amd26111SourceInventory.ParsePinned26111(settings.Original194ManifestText);
        _settings = settings;
    }

    /// <summary>
    /// Executes real Windows Steps 1-7 ONLY, including recovery export,
    /// verified original AMD source, profile candidate and unsigned catalog.
    /// The result is NOT signed and NOT installable. No pnputil /add-driver,
    /// SetupAPI bind, certificate import or restart is ever requested.
    /// </summary>
    public async Task<PreparedUnsignedReceipt> PrepareUnsignedAsync(
        Action<M195LifecycleSequence.Step, string>? progress = null,
        CancellationToken token = default)
    {
        void Update(M195LifecycleSequence.Step step, string text) => progress?.Invoke(step, text);

        Update(M195LifecycleSequence.Step.IdentifyDevice, "Checking your device");
        var device = await IdentifyExactDeviceAsync(token);
        Update(M195LifecycleSequence.Step.InspectDriver, "Inspecting the current driver");
        var active = await InspectAssignedDriverAsync(device, token);
        Update(M195LifecycleSequence.Step.BackupDriver, "Preserving the current graphics driver");
        var baseline = await ExportBaselineAsync(device, active, token);
        if (device.Profile != HandheldProfiles.Go1 && !baseline.Verified)
            throw new InvalidDataException("M195_REAL_TESTER_BASELINE_NOT_VERIFIED");

        Update(M195LifecycleSequence.Step.AcquireAmdDriver, "Verifying the AMD installer");
        var installer = await AcquireVerifiedAmdInstallerAsync(token);
        Update(M195LifecycleSequence.Step.ExtractAmdDriver, "Extracting original AMD files");
        var extracted = await ExtractOriginalAmdAsync(installer, token);
        Update(M195LifecycleSequence.Step.PrepareDeviceDriver, "Preparing the graphics driver");
        var prepared = await BuildUnsignedCandidateAsync(device, installer, extracted, token);
        Update(M195LifecycleSequence.Step.GenerateCatalog, "Generating an unsigned catalog");
        var unsignedCatalog = await GenerateUnsignedCatalogAsync(prepared, token);

        return new PreparedUnsignedReceipt(
            device, active, baseline, installer, extracted, prepared, unsignedCatalog);
    }

    public async Task<M195LifecycleSequence.Device> IdentifyExactDeviceAsync(
        CancellationToken token)
    {
        var displays = await M195WindowsGpuInventory.EnumerateAsync(token);
        M195WindowsGpuInventory.ExactGpu gpu =
            M195WindowsGpuInventory.RequireUniqueExactGpu(displays);
        _observed = gpu;
        return new M195LifecycleSequence.Device(
            gpu.Display.InstanceId, gpu.Profile.HardwareId, gpu.Profile);
    }

    public Task<M195LifecycleSequence.AssignedDriver> InspectAssignedDriverAsync(
        M195LifecycleSequence.Device device, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var gpu = RequireObservedGpu(device);
        if (string.IsNullOrWhiteSpace(gpu.Display.PublishedInf) ||
            string.IsNullOrWhiteSpace(gpu.Display.Version))
            throw new InvalidDataException("M195_NATIVE_ASSIGNED_DRIVER_IDENTITY_MISSING");
        bool eligible = M195WindowsGpuInventory.EligibleForRecoveryExport(gpu.Display);
        return Task.FromResult(new M195LifecycleSequence.AssignedDriver(
            gpu.Display.PublishedInf, gpu.Display.Version,
            checked((int)gpu.Display.ProblemCode), eligible,
            eligible ? "Verified healthy OEM INF eligible for export"
                     : "Active driver is not eligible for healthy OEM export"));
    }

    public async Task<M195LifecycleSequence.Baseline> ExportBaselineAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver assigned, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(assigned);
        var gpu = RequireObservedGpu(device);
        if (!string.Equals(gpu.Display.PublishedInf, assigned.PublishedInf,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_RECOVERY_ASSIGNED_INF_CHANGED");
        if (!assigned.BackupEligible)
        {
            if (gpu.Profile != HandheldProfiles.Go1)
                throw new InvalidDataException("M195_RECOVERY_TESTER_REQUIRES_ELIGIBLE_GPU");
            return new M195LifecycleSequence.Baseline(null, false, null);
        }
        M195WindowsRecoveryBaseline.VerifiedExport exported =
            await M195WindowsRecoveryBaseline.ExportAsync(
                gpu, _settings.FreshRecoveryRoot, token);
        if (exported.VerifiedFiles < 1 ||
            !string.Equals(exported.OriginalInf, assigned.PublishedInf,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(exported.ManifestPath))
            throw new InvalidDataException("M195_RECOVERY_EXPORT_RECEIPT_FAILED");
        return new M195LifecycleSequence.Baseline(
            exported.PackageDirectory, true, assigned.PublishedInf);
    }

    public Task<OfficialAmdInstallerVerifier.VerifiedSource>
        AcquireVerifiedAmdInstallerAsync(CancellationToken token)
    {
        if (!string.IsNullOrWhiteSpace(_settings.LocalOfficialAmdInstaller))
            return OfficialAmdInstallerVerifier.Verify26111Async(
                _settings.LocalOfficialAmdInstaller, token);
        return OfficialAmd26111Downloader.DownloadFreshAsync(
            _settings.FreshOfficialDownloadRoot,
            cancellationToken: token);
    }

    public Task<Amd26111ArchiveExtraction.VerifiedExtraction> ExtractOriginalAmdAsync(
        OfficialAmdInstallerVerifier.VerifiedSource source, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Sha256 !=
            OfficialAmd26111SourceContract.VerifiedFrozenM194.InstallerSha256)
            throw new InvalidDataException("M195_EXTRACT_RECEIPT_NOT_AMD26111");
        return Amd26111ArchiveExtraction.ExtractAsync(
            source.FullPath, _settings.Pinned7zrExePath,
            _settings.FreshOriginalExtractionRoot,
            _settings.Original194ManifestText, token);
    }

    public Task<Amd26111TwoPhasePreparation.PreparationReceipt> BuildUnsignedCandidateAsync(
        M195LifecycleSequence.Device device,
        OfficialAmdInstallerVerifier.VerifiedSource source,
        Amd26111ArchiveExtraction.VerifiedExtraction extracted,
        CancellationToken token)
    {
        var gpu = RequireObservedGpu(device);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(extracted);
        if (source.Sha256 != OfficialAmd26111SourceContract.VerifiedFrozenM194.InstallerSha256 ||
            extracted.OriginalFilesVerified != 194 ||
            extracted.SourceManifestSha256 !=
                Amd26111SourceInventory.PinnedOriginalManifestSha256)
            throw new InvalidDataException("M195_PREPARE_SOURCE_RECEIPT_INVALID");
        return Amd26111TwoPhasePreparation.VerifyAndStageAsync(
            source.FullPath, extracted.DisplayRoot,
            _settings.Original194ManifestText, _settings.AuthorizedProfilePolicyRows,
            gpu.Profile, _settings.FreshUnsignedCandidateRoot, token);
    }

    public async Task<M195LifecycleSequence.GeneratedCatalog> GenerateUnsignedCatalogAsync(
        Amd26111TwoPhasePreparation.PreparationReceipt prepared, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        var result = await Amd26111UnsignedCatalogGenerator.GenerateAsync(
            prepared.UnsignedStage, prepared.Candidate,
            _settings.Original194ManifestText, _settings.PinnedInf2CatExePath, token);
        return new M195LifecycleSequence.GeneratedCatalog(
            result.UnsignedCatalogPath, result.Sha256,
            result.VerifiedDriverFiles, result.CatalogContainsSigners);
    }

    private M195WindowsGpuInventory.ExactGpu RequireObservedGpu(
        M195LifecycleSequence.Device device)
    {
        ArgumentNullException.ThrowIfNull(device);
        M195WindowsGpuInventory.ExactGpu match = _observed ??
            throw new InvalidOperationException("M195_NATIVE_DEVICE_HAS_NOT_BEEN_IDENTIFIED");
        if (!string.Equals(device.InstanceId, match.Display.InstanceId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(device.ExactHardwareId, match.Profile.HardwareId,
                StringComparison.OrdinalIgnoreCase) ||
            device.Profile != match.Profile)
            throw new InvalidDataException("M195_NATIVE_DEVICE_PROFILE_CROSSOVER");
        return match;
    }

    // The user-facing release MUST NOT silently use fake sign/bind/MSI/reboot
    // implementations. These remain explicit until audited native M175/M179
    // and the full AMD Software / first postboot recovery code is ported.
    public Task<M195LifecycleSequence.BoundDriver> SignOnceStageBindAndVerifyDualTrustAsync(
        M195LifecycleSequence.Device device,
        Amd26111TwoPhasePreparation.PreparationReceipt prepared,
        M195LifecycleSequence.GeneratedCatalog generated,
        M195LifecycleSequence.Baseline baseline, CancellationToken token) =>
        throw new NotSupportedException("M195_NATIVE_SIGN_BIND_DUAL_TRUST_NOT_YET_PORTED");

    public Task<M195LifecycleSequence.PrebootObservation> ObservePrebootAsync(
        M195LifecycleSequence.Device device, M195LifecycleSequence.BoundDriver bound,
        CancellationToken token) =>
        M195WindowsPrebootObservation.ObserveAsync(device, bound, token);

    public Task<M195LifecycleSequence.SoftwareResult> InstallVerifiedAmdSoftwareAsync(
        OfficialAmdInstallerVerifier.VerifiedSource source,
        M195LifecycleSequence.BoundDriver bound, CancellationToken token) =>
        throw new NotSupportedException("M195_NATIVE_ADRENALIN_INSTALL_NOT_YET_PORTED");

    public Task<M195LifecycleSequence.RegisteredPostboot> RegisterPostbootAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline,
        M195LifecycleSequence.BoundDriver bound, CancellationToken token) =>
        throw new NotSupportedException("M195_NATIVE_POSTBOOT_REGISTRATION_NOT_YET_PORTED");

    public Task<string> ReadPreRebootBootIdentityAsync(CancellationToken token) =>
        throw new NotSupportedException("M195_NATIVE_BOOT_IDENTITY_NOT_YET_PORTED");

    public Task RequestSingleRestartAsync(
        M195LifecycleSequence.RegisteredPostboot registration, CancellationToken token) =>
        throw new NotSupportedException("M195_NATIVE_REBOOT_NOT_YET_PORTED");

    public Task<M195LifecycleSequence.RecoveryResult> RestoreBaselineAfterPrebootFailureAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline, CancellationToken token) =>
        throw new NotSupportedException("M195_NATIVE_PREBOOT_ROLLBACK_NOT_YET_PORTED");
}
