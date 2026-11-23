using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// INTERNAL source-first M195 adapter that stitches together the ACTUAL
/// Step1-7 receipt objects with independently ported native Steps 8, 10, 11.
/// It is deliberately not reachable through any published executable or GUI.
/// No public entrypoint, SDK end-user dependency or live hardware test is
/// implied by successful offline compilation.
/// </summary>
internal sealed class M195WindowsFullLifecycleActions :
    M195LifecycleSequence.IWindowsInstallationActions
{
    internal sealed record Settings(
        M195WindowsPreparationActions.Settings Preparation,
        string VerifiedSignToolExe,
        string VerifiedPortable7ZipReleaseExe,
        string FreshSoftwareWorkspace,
        string FreshSoftwareEvidenceRoot,
        string FreshPrebootRecoveryEvidenceRoot);

    private readonly Settings _settings;
    private readonly M195WindowsPreparationActions _preparation;
    private M195LifecycleSequence.Device? _device;
    private M195LifecycleSequence.AssignedDriver? _original;
    private M195LifecycleSequence.Baseline? _baseline;
    private OfficialAmdInstallerVerifier.VerifiedSource? _amd;
    private Amd26111ArchiveExtraction.VerifiedExtraction? _extraction;
    private Amd26111TwoPhasePreparation.PreparationReceipt? _candidate;
    private Amd26111UnsignedCatalogGenerator.Result? _unsignedResult;
    private M195LifecycleSequence.GeneratedCatalog? _unsigned;
    private M195LifecycleSequence.BoundDriver? _bound;
    private M195WindowsStep10Native.Result? _software;

    internal M195WindowsFullLifecycleActions(Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settings.Preparation);
        foreach (string path in new[] {
            settings.VerifiedSignToolExe, settings.VerifiedPortable7ZipReleaseExe,
            settings.FreshSoftwareWorkspace, settings.FreshSoftwareEvidenceRoot,
            settings.FreshPrebootRecoveryEvidenceRoot
        })
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (string.IsNullOrWhiteSpace(settings.Preparation.FreshPostbootEvidenceRoot))
            throw new InvalidDataException("M195_FULL_ADAPTER_POSTBOOT_EVIDENCE_ROOT_REQUIRED");
        _settings = settings;
        _preparation = new M195WindowsPreparationActions(settings.Preparation);
    }

    public async Task<M195LifecycleSequence.Device> IdentifyExactDeviceAsync(
        CancellationToken token)
    {
        RequireNotStarted();
        _device = await _preparation.IdentifyExactDeviceAsync(token);
        return _device;
    }

    public async Task<M195LifecycleSequence.AssignedDriver> InspectAssignedDriverAsync(
        M195LifecycleSequence.Device device, CancellationToken token)
    {
        RequireSame(_device, device, "GPU_IDENTITY");
        if (_original is not null)
            throw new InvalidOperationException("M195_FULL_ORIGINAL_ALREADY_INSPECTED");
        _original = await _preparation.InspectAssignedDriverAsync(device, token);
        return _original;
    }

    public async Task<M195LifecycleSequence.Baseline> ExportBaselineAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver assigned, CancellationToken token)
    {
        RequireSame(_device, device, "EXPORT_DEVICE");
        RequireSame(_original, assigned, "ORIGINAL_RECEIPT");
        if (_baseline is not null)
            throw new InvalidOperationException("M195_FULL_BASELINE_ALREADY_EXPORTED");
        _baseline = await _preparation.ExportBaselineAsync(device, assigned, token);
        return _baseline;
    }

    public async Task<OfficialAmdInstallerVerifier.VerifiedSource>
        AcquireVerifiedAmdInstallerAsync(CancellationToken token)
    {
        if (_baseline is null || _amd is not null)
            throw new InvalidOperationException("M195_FULL_ACQUIRE_OUT_OF_ORDER");
        _amd = await _preparation.AcquireVerifiedAmdInstallerAsync(token);
        return _amd;
    }

    public async Task<Amd26111ArchiveExtraction.VerifiedExtraction>
        ExtractOriginalAmdAsync(
            OfficialAmdInstallerVerifier.VerifiedSource source, CancellationToken token)
    {
        RequireSame(_amd, source, "AUTHENTICATED_AMD_SOURCE");
        if (_extraction is not null)
            throw new InvalidOperationException("M195_FULL_EXTRACT_ALREADY_COMPLETE");
        _extraction = await _preparation.ExtractOriginalAmdAsync(source, token);
        return _extraction;
    }

    public async Task<Amd26111TwoPhasePreparation.PreparationReceipt>
        BuildUnsignedCandidateAsync(
            M195LifecycleSequence.Device device,
            OfficialAmdInstallerVerifier.VerifiedSource source,
            Amd26111ArchiveExtraction.VerifiedExtraction extracted,
            CancellationToken token)
    {
        RequireSame(_device, device, "PREPARE_DEVICE");
        RequireSame(_amd, source, "PREPARE_AUTHENTICATED_AMD");
        RequireSame(_extraction, extracted, "PREPARE_ORIGINAL_EXTRACT");
        if (_candidate is not null)
            throw new InvalidOperationException("M195_FULL_CANDIDATE_ALREADY_PREPARED");
        _candidate = await _preparation.BuildUnsignedCandidateAsync(
            device, source, extracted, token);
        return _candidate;
    }

    public async Task<M195LifecycleSequence.GeneratedCatalog>
        GenerateUnsignedCatalogAsync(
            Amd26111TwoPhasePreparation.PreparationReceipt prepared,
            CancellationToken token)
    {
        RequireSame(_candidate, prepared, "GENERATE_CANDIDATE");
        if (_unsignedResult is not null)
            throw new InvalidOperationException("M195_FULL_INF2CAT_ALREADY_GENERATED");

        // Preserve the REAL process exit, stdout/stderr logs and all source
        // hashes. Never fabricate an Inf2Cat.Result from its reduced DTO.
        _unsignedResult = await Amd26111UnsignedCatalogGenerator.GenerateAsync(
            prepared.UnsignedStage, prepared.Candidate,
            _settings.Preparation.Original194ManifestText,
            _settings.Preparation.PinnedInf2CatExePath, token);
        _unsigned = new M195LifecycleSequence.GeneratedCatalog(
            _unsignedResult.UnsignedCatalogPath, _unsignedResult.Sha256,
            _unsignedResult.VerifiedDriverFiles,
            _unsignedResult.CatalogContainsSigners);
        return _unsigned;
    }

    public async Task<M195LifecycleSequence.BoundDriver>
        SignOnceStageBindAndVerifyDualTrustAsync(
            M195LifecycleSequence.Device device,
            Amd26111TwoPhasePreparation.PreparationReceipt prepared,
            M195LifecycleSequence.GeneratedCatalog generated,
            M195LifecycleSequence.Baseline baseline, CancellationToken token)
    {
        RequireSame(_device, device, "BIND_DEVICE");
        RequireSame(_candidate, prepared, "BIND_CANDIDATE");
        RequireSame(_baseline, baseline, "BIND_RECOVERY_BASELINE");
        RequireSame(_unsigned, generated, "INF2CAT_REAL_RECEIPT");
        if (_unsignedResult is null || _original is null || _bound is not null)
            throw new InvalidOperationException("M195_FULL_BIND_OUT_OF_ORDER");
        var native = await M195WindowsStep8Native.ExecuteAsync(
            device, _original, baseline, prepared, _unsignedResult,
            _settings.Preparation.Original194ManifestText,
            _settings.VerifiedSignToolExe, token);
        _bound = native.Bound;
        return native.Bound;
    }

    public Task<M195LifecycleSequence.PrebootObservation> ObservePrebootAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.BoundDriver bound, CancellationToken token)
    {
        RequireSame(_device, device, "PREBOOT_DEVICE");
        RequireSame(_bound, bound, "PREBOOT_BOUND_DRIVER");
        return _preparation.ObservePrebootAsync(device, bound, token);
    }

    public async Task<M195LifecycleSequence.SoftwareResult>
        InstallVerifiedAmdSoftwareAsync(
            OfficialAmdInstallerVerifier.VerifiedSource source,
            M195LifecycleSequence.BoundDriver bound, CancellationToken token)
    {
        RequireSame(_amd, source, "SOFTWARE_VERIFIED_AMD_SOURCE");
        RequireSame(_bound, bound, "SOFTWARE_BOUND_TARGET");
        if (_device is null || _extraction is null || _software is not null)
            throw new InvalidOperationException("M195_FULL_SOFTWARE_OUT_OF_ORDER");
        _software = await M195WindowsStep10Native.ExecuteAsync(
            _device, bound, source, _extraction,
            _settings.Preparation.Pinned7zrExePath,
            _settings.VerifiedPortable7ZipReleaseExe,
            _settings.FreshSoftwareWorkspace,
            _settings.FreshSoftwareEvidenceRoot, token);
        return _software.Software;
    }

    public Task<string> ReadPreRebootBootIdentityAsync(CancellationToken token)
    {
        RequireSoftware();
        return _preparation.ReadPreRebootBootIdentityAsync(token);
    }

    public Task<M195LifecycleSequence.RegisteredPostboot> RegisterPostbootAsync(
        M195LifecycleSequence.PendingPostboot pending,
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline, CancellationToken token)
    {
        RequireSoftware();
        RequireSame(_original, original, "REGISTER_ORIGINAL_DRIVER");
        RequireSame(_baseline, baseline, "REGISTER_BASELINE");
        if (_device is null || _bound is null || _amd is null ||
            !string.Equals(pending.InstanceId, _device.InstanceId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(pending.ExactHardwareId, _device.ExactHardwareId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(pending.ExpectedPublishedInf, _bound.PublishedInf,
                StringComparison.OrdinalIgnoreCase) ||
            pending.ExpectedDriverVersion != _bound.TargetDriverVersion ||
            pending.ExpectedMainCatalogSha256 != _bound.MainSignedCatalogSha256 ||
            pending.ExpectedInstallerSha256 != _amd.Sha256)
            throw new InvalidDataException("M195_FULL_POSTBOOT_TICKET_NOT_ACTUAL_BOUND_DEVICE");
        M195PostbootTicketContract.RequireValid(pending);
        return _preparation.RegisterPostbootAsync(pending, original, baseline, token);
    }

    public Task RequestSingleRestartAsync(
        M195LifecycleSequence.RegisteredPostboot registration,
        CancellationToken token)
    {
        RequireSoftware();
        return _preparation.RequestSingleRestartAsync(registration, token);
    }

    public async Task<M195LifecycleSequence.RecoveryResult>
        RestoreBaselineAfterPrebootFailureAsync(
            M195LifecycleSequence.Device device,
            M195LifecycleSequence.AssignedDriver original,
            M195LifecycleSequence.Baseline baseline, CancellationToken token)
    {
        RequireSame(_device, device, "ROLLBACK_DEVICE");
        RequireSame(_original, original, "ROLLBACK_ORIGINAL_DRIVER");
        RequireSame(_baseline, baseline, "ROLLBACK_EXPORTED_BASELINE");
        if (_bound is null)
            throw new InvalidOperationException("M195_FULL_ROLLBACK_BEFORE_BIND");

        var recovery = await M195WindowsNativeBind.RestoreAfterPrebootFailureAsync(
            device, _bound, original, baseline, token);
        string root = Path.GetFullPath(_settings.FreshPrebootRecoveryEvidenceRoot);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("M195_FULL_ROLLBACK_EVIDENCE_ALREADY_PRESENT");
        Directory.CreateDirectory(root);
        string receipt = Path.Combine(root, "M195-Preboot-Recovery.json");
        await File.WriteAllTextAsync(receipt,
            JsonSerializer.Serialize(new {
                Schema = "Finesse.M195.PrebootRecovery.v1",
                OriginalInf = original.PublishedInf,
                OriginalVersion = original.Version,
                TargetInf = _bound.PublishedInf,
                TargetVersion = _bound.TargetDriverVersion,
                Native = recovery,
                ActualBaselineCode0Confirmed = recovery.ConfirmedCode0,
                ExtraRestartRequested = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false), CancellationToken.None);
        return new M195LifecycleSequence.RecoveryResult(
            recovery.ConfirmedCode0, recovery.ObservedInf,
            recovery.ObservedProblemCode is null ? null :
                checked((int)recovery.ObservedProblemCode.Value));
    }

    private void RequireNotStarted()
    {
        if (_device is not null)
            throw new InvalidOperationException("M195_FULL_DEVICE_ALREADY_IDENTIFIED");
    }

    private void RequireSoftware()
    {
        if (_software is null || !_software.Software.InstalledAndVerified ||
            !_software.Installed.InstalledAndVerified)
            throw new InvalidOperationException("M195_FULL_SOFTWARE_NOT_VERIFIED_BEFORE_RESTART");
    }

    private static void RequireSame<T>(T? actual, T requested, string field)
        where T : class
    {
        if (actual is null || !ReferenceEquals(actual, requested))
            throw new InvalidDataException("M195_FULL_RECEIPT_OBJECT_DRIFT_" + field);
    }
}
