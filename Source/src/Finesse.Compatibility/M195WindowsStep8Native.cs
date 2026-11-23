using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Internal M195 native implementation of M179/M191 Step 8. It consumes the
/// actual source/Inf2Cat receipts and uses one retained signer, SetupAPI staging,
/// NewDev binding, and the original/local 14x4 catalog evidence. It is NOT
/// connected to the production GUI or M195WindowsPreparationActions: Steps
/// 10-12 (AMD Software then single reboot then postboot recovery) are unfinished.
/// </summary>
internal static class M195WindowsStep8Native
{
    internal static async Task<M195Step8Sequence.Outcome> ExecuteAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline,
        Amd26111TwoPhasePreparation.PreparationReceipt prepared,
        Amd26111UnsignedCatalogGenerator.Result generated,
        string original194ManifestText,
        string verifiedSignToolExe,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(generated);

        // The complete, real Windows Step-7 result is mandatory. Do not synthesize
        // ExitCode=0 or a success receipt from a GUI-only DTO.
        M195LiveSigningPreflight.Verified preflight =
            M195LiveSigningPreflight.Validate(
                prepared.UnsignedStage, prepared.Candidate, generated,
                original194ManifestText, verifiedSignToolExe);

        if (prepared.Candidate.Profile != device.Profile ||
            prepared.Installer.Sha256 !=
                OfficialAmd26111SourceContract.VerifiedFrozenM194.InstallerSha256)
            throw new InvalidDataException("M195_STEP8_PREPARED_RECEIPT_WRONG_PROFILE_OR_RELEASE");

        string receipt = Path.Combine(preflight.EvidenceDirectory,
            "M195-Step8-Transaction.json");
        if (File.Exists(receipt))
            throw new IOException("M195_STEP8_TRANSACTION_ALREADY_PRESENT");
        var adapter = new WindowsPhases(preflight, device, original, baseline);
        try
        {
            var result = await M195Step8Sequence.ExecuteAsync(
                new M195Step8Sequence.Context(device, original, baseline),
                adapter, token);
            await WriteReceiptAsync(receipt, new {
                Schema = "Finesse.M195.Step8.v1",
                Status = "PREBOOT_BOUND_NOT_POSTBOOT_VERIFIED",
                Device = device.ExactHardwareId,
                DeviceInstance = device.InstanceId,
                OriginalInf = original.PublishedInf,
                Bound = result.Bound,
                DualTrust = result.Trust,
                SigningEvidence = adapter.SignerEvidence,
                StagingEvidence = adapter.StageEvidence,
                BindEvidence = adapter.BindEvidence,
                RecoveryEvidence = adapter.RecoveryEvidence,
                WindowsRestartRequested = false,
                AmdSoftwareInstalled = false,
                TargetPostbootCode0Confirmed = false
            });
            return result;
        }
        catch (Exception error)
        {
            await WriteReceiptAsync(receipt, new {
                Schema = "Finesse.M195.Step8.v1",
                Status = "FAILED_REVIEW_RECORDED_RECOVERY",
                Device = device.ExactHardwareId,
                DeviceInstance = device.InstanceId,
                Exception = error.GetType().Name + ": " + error.Message,
                SigningEvidence = adapter.SignerEvidence,
                StagingEvidence = adapter.StageEvidence,
                BindEvidence = adapter.BindEvidence,
                RecoveryEvidence = adapter.RecoveryEvidence,
                WindowsRestartRequested = false,
                AmdSoftwareInstalled = false,
                TargetPostbootCode0Confirmed = false
            });
            throw;
        }
    }

    private sealed class WindowsPhases : M195Step8Sequence.IActions
    {
        private readonly M195LiveSigningPreflight.Verified _verified;
        private readonly M195LifecycleSequence.Device _device;
        private readonly M195LifecycleSequence.AssignedDriver _original;
        private readonly M195LifecycleSequence.Baseline _baseline;
        private M195LiveCatalogSigner.Signed? _signer;
        private M195WindowsDriverStoreStage.StageReceipt? _stage;
        private M195WindowsNativeBind.BindReceipt? _bind;

        internal string? SignerEvidence => _signer?.EvidencePath;
        internal string? StageEvidence => _stage?.EvidenceFile;
        internal string? BindEvidence => _bind?.EvidencePath;
        internal object? RecoveryEvidence { get; private set; }

        internal WindowsPhases(
            M195LiveSigningPreflight.Verified verified,
            M195LifecycleSequence.Device device,
            M195LifecycleSequence.AssignedDriver original,
            M195LifecycleSequence.Baseline baseline)
        {
            _verified = verified;
            _device = device;
            _original = original;
            _baseline = baseline;
        }

        public async Task<M195Step8Sequence.SignedPackage> SignOnceAsync(
            CancellationToken token)
        {
            if (_signer is not null)
                throw new InvalidOperationException("M195_STEP8_SIGNER_ALREADY_CREATED");
            _signer = await M195LiveCatalogSigner.SignAsync(_verified, token);
            return new(_signer.SignedCatalogSha256, true, _signer.TrustRetained);
        }

        public async Task<M195Step8Sequence.StagedPackage> StageAsync(
            M195Step8Sequence.SignedPackage signed, CancellationToken token)
        {
            if (_signer is null || _stage is not null ||
                !signed.CatalogSha256.Equals(_signer.SignedCatalogSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("M195_STEP8_SIGN_STAGE_SEQUENCE_INVALID");
            _stage = await M195WindowsDriverStoreStage.StageOnlyAsync(
                _verified, _signer, _device, _baseline, token);
            _ = M195WindowsDriverStoreLocator.RequireFinesse26111Package(
                _stage.PublishedInf, _verified.PreparedInf,
                _signer.CatalogPath, _signer.SignedCatalogSha256);
            return new(_stage.PublishedInf, _stage.SignedCatalogSha256, true);
        }

        public async Task<M195Step8Sequence.BoundAssignment> BindAsync(
            M195Step8Sequence.StagedPackage staged, CancellationToken token)
        {
            if (_stage is null || _signer is null || _bind is not null ||
                !string.Equals(staged.PublishedInf, _stage.PublishedInf,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("M195_STEP8_STAGE_BIND_SEQUENCE_INVALID");
            // BindAsync internally attempts and records recovery if a native
            // call fails or the assigned active GPU does not match.
            _bind = await M195WindowsNativeBind.BindAsync(
                _verified, _signer, _stage, _device, _original, _baseline, token);
            return new(_bind.ObservedPublishedInf, _bind.ObservedVersion,
                checked((int)_bind.ObservedProblemCode), _bind.RebootRequired,
                _bind.NativeAccepted);
        }

        public async Task<M195Step8Sequence.TrustEvidence>
            AuditAndRegisterOriginalAsync(
                M195Step8Sequence.BoundAssignment bound, CancellationToken token)
        {
            if (_bind is null || _stage is null || _signer is null)
                throw new InvalidOperationException("M195_STEP8_AUDIT_BEFORE_BIND");
            string folder = _verified.EvidenceDirectory;
            string beforePath = Path.Combine(folder,
                "M195-Step8-DualCatalog-Initial.json");
            M195WindowsDualCatalogAudit.Audit initial;
            try
            {
                initial = await M195WindowsDualCatalogAudit.CaptureAsync(
                    _device, bound.PublishedInf, _verified.PackageRoot,
                    _verified.OriginalAmdCatalog,
                    _signer.SignedCatalogSha256, _verified.PinnedSignTool,
                    beforePath, token);
            }
            catch (Exception ex) when (!CriticalIntegrity(ex))
            {
                return await WarningAsync(folder, ex, null);
            }

            M195DualCatalogCoverage.Assessment current = initial.Assessment;
            string? warning = null;
            string evidence = initial.EvidenceFile;
            if (current.OriginalCatalogRegistrationSuggested)
            {
                try
                {
                    string freshFolder = Path.Combine(folder,
                        "M195-Step8-OriginalCatalogRegistration");
                    var registration =
                        await M195OriginalAmdCatalogRegistration.EnsureOriginalRegisteredAsync(
                            _device, bound.PublishedInf, _verified.PackageRoot,
                            _verified.OriginalAmdCatalog,
                            _signer.SignedCatalogSha256, _verified.PinnedSignTool,
                            freshFolder, token);
                    current = registration.FinalAssessment;
                    evidence = registration.AfterEvidence;
                }
                catch (Exception ex) when (!CriticalIntegrity(ex))
                {
                    warning = "ORIGINAL_CATALOG_REGISTRATION_INCOMPLETE:" +
                        ex.GetType().Name + ":" + ex.Message;
                    await WarningAsync(folder, ex, initial);
                }
            }
            return MapAssessment(current,
                initial.Snapshot.OriginalCatalogAuthenticodeWhcp,
                evidence, warning);
        }

        public async Task RestoreAfterPostBindFailureAsync(
            M195Step8Sequence.BoundAssignment bound, Exception originalError)
        {
            if (_bind is null)
                throw new InvalidOperationException("M195_STEP8_RECOVERY_WITHOUT_BOUND_GPU");
            var restored = await M195WindowsNativeBind.RestoreAfterStep8FailureAsync(
                _bind, _original, _baseline);
            RecoveryEvidence = new {
                OriginalFailure = originalError.GetType().Name + ": " + originalError.Message,
                restored.ApiAccepted, restored.WindowsError,
                restored.RebootRequired, restored.ObservedInf,
                restored.ObservedVersion, restored.ObservedProblemCode,
                restored.ConfirmedCode0, restored.Status
            };
            // A nonverified recovery is never a successful rollback, even
            // when NewDev reported that the request was accepted.
            if (!restored.ConfirmedCode0)
                throw new InvalidDataException(
                    "M195_STEP8_RECOVERY_UNVERIFIED:" + restored.Status);
        }

        private static M195Step8Sequence.TrustEvidence MapAssessment(
            M195DualCatalogCoverage.Assessment x, bool whcp,
            string path, string? warning) =>
            new(x.OriginalCoverage, x.LocalCoverage,
                x.RegisteredOriginalCoverage, x.RegisteredLocalCoverage,
                x.DualTrustVerified, whcp, path,
                warning ?? (x.DualTrustVerified ? null :
                    "DUAL_TRUST_14_OF_14_INCOMPLETE_REVIEW_EVIDENCE"));

        private static async Task<M195Step8Sequence.TrustEvidence> WarningAsync(
            string folder, Exception error,
            M195WindowsDualCatalogAudit.Audit? initial)
        {
            string evidence = Path.Combine(folder,
                "M195-Step8-DualCatalog-Warning.json");
            await WriteReceiptAsync(evidence, new {
                Schema = "Finesse.M195.Step8.AuditWarning.v1",
                Error = error.GetType().Name + ":" + error.Message,
                OriginalEvidence = initial?.EvidenceFile,
                SourceBytesAlteredKnown = false,
                DriverBindAlreadyAttempted = true,
                AutomaticRebootRequested = false
            });
            if (initial is null)
                return new(0, 0, 0, 0, false, false, evidence,
                    "DUAL_TRUST_AUDIT_UNAVAILABLE:" +
                    error.GetType().Name + ":" + error.Message);
            return MapAssessment(initial.Assessment,
                initial.Snapshot.OriginalCatalogAuthenticodeWhcp,
                evidence, "ORIGINAL_CATALOG_REGISTRATION_INCOMPLETE:" +
                    error.GetType().Name + ":" + error.Message);
        }

        private static bool CriticalIntegrity(Exception error)
        {
            if (error is not InvalidDataException) return false;
            string s = error.Message;
            return s.StartsWith("M195_AUDIT_GRAPHICS_HASH_MISMATCH", StringComparison.Ordinal) ||
                s.StartsWith("M195_AUDIT_MEMBER_ESCAPED_DRIVERSTORE", StringComparison.Ordinal) ||
                s.StartsWith("M195_DRIVERSTORE_", StringComparison.Ordinal) ||
                s.StartsWith("M195_AUDIT_CATALOG_OR_SIGNTOOL_SHA_FAILED", StringComparison.Ordinal) ||
                s.StartsWith("M195_AUDIT_PUBLISHED_INF_NOT_ASSIGNED_TO_TARGET_GPU", StringComparison.Ordinal) ||
                s.StartsWith("M195_REGISTRATION_SOURCE_CAT_CHANGED", StringComparison.Ordinal) ||
                s.StartsWith("M195_REGISTRATION_INSTALLED_CAT_HASH_MISMATCH", StringComparison.Ordinal) ||
                s.StartsWith("M195_REGISTRATION_MANAGED_NAME_COLLISION", StringComparison.Ordinal);
        }
    }

    private static async Task WriteReceiptAsync(string path, object evidence)
    {
        string temp = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp,
                JsonSerializer.Serialize(evidence,
                    new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), CancellationToken.None);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
