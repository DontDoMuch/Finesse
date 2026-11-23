namespace Finesse.Compatibility;

/// <summary>
/// Vendor-independent ordering for the frozen M179 + M191 Step 8.
/// The only production implementation is internal, and no GUI path calls it.
/// This coordinator never invokes native Windows APIs on its own.
/// </summary>
public static class M195Step8Sequence
{
    public sealed record Context(
        M195LifecycleSequence.Device Device,
        M195LifecycleSequence.AssignedDriver Original,
        M195LifecycleSequence.Baseline Baseline);

    public sealed record SignedPackage(string CatalogSha256,
        bool SignedOnce, bool TrustRetained);
    public sealed record StagedPackage(string PublishedInf, string CatalogSha256,
        bool PublishedInfAndCatalogVerified);
    public sealed record BoundAssignment(string PublishedInf, string DriverVersion,
        int ProblemCode, bool RebootRequired, bool NativeAccepted);
    public sealed record TrustEvidence(int OriginalFileCoverage, int LocalFileCoverage,
        int RegisteredOriginalCoverage, int RegisteredLocalCoverage,
        bool FullDualTrustVerified, bool OriginalWhcpVerified,
        string EvidencePath, string? Warning);
    public sealed record Outcome(M195LifecycleSequence.BoundDriver Bound,
        TrustEvidence Trust, bool VerifiedPrebootAssignment);

    public interface IActions
    {
        Task<SignedPackage> SignOnceAsync(CancellationToken token);
        Task<StagedPackage> StageAsync(SignedPackage signed, CancellationToken token);
        Task<BoundAssignment> BindAsync(StagedPackage staged, CancellationToken token);
        Task<TrustEvidence> AuditAndRegisterOriginalAsync(
            BoundAssignment bound, CancellationToken token);
        Task RestoreAfterPostBindFailureAsync(
            BoundAssignment bound, Exception originalError);
    }

    /// <summary>
    /// Full 14x4 evidence is recorded, not an unconditional M194 installation
    /// gate. A negative audit result is NEVER silently described as 14/14.
    /// A native hash/DriverStore tamper exception is a separate hard failure
    /// requiring an attempted recovery after binding.
    /// </summary>
    public static async Task<Outcome> ExecuteAsync(
        Context context, IActions actions, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Device);
        ArgumentNullException.ThrowIfNull(context.Original);
        ArgumentNullException.ThrowIfNull(context.Baseline);
        ArgumentNullException.ThrowIfNull(actions);
        if (context.Device.Profile !=
            HandheldProfiles.Resolve(context.Device.ExactHardwareId) ||
            string.IsNullOrWhiteSpace(context.Device.InstanceId) ||
            string.IsNullOrWhiteSpace(context.Original.Version) ||
            string.IsNullOrWhiteSpace(context.Original.PublishedInf) ||
            (context.Device.Profile != HandheldProfiles.Go1 &&
             (!context.Original.BackupEligible ||
              !context.Baseline.Verified ||
              string.IsNullOrWhiteSpace(context.Baseline.VerifiedExportDirectory))) ||
            (context.Baseline.Verified &&
             (string.IsNullOrWhiteSpace(context.Baseline.VerifiedExportDirectory) ||
              !string.Equals(context.Baseline.RecoveryInf,
                  context.Original.PublishedInf, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("M195_STEP8_BASELINE_POLICY_NOT_SATISFIED");

        SignedPackage signed = await actions.SignOnceAsync(token);
        if (signed is null || !signed.SignedOnce || !signed.TrustRetained ||
            !HasSha(signed.CatalogSha256))
            throw new InvalidDataException("M195_STEP8_SIGNING_NOT_CONFIRMED");

        StagedPackage stage = await actions.StageAsync(signed, token);
        if (stage is null || !stage.PublishedInfAndCatalogVerified ||
            !string.Equals(stage.CatalogSha256, signed.CatalogSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_STEP8_STAGE_NOT_CONFIRMED");
        string published = M195WindowsBindIdentity.RequirePublishedOemInf(
            stage.PublishedInf);

        // BindAsync must implement its own failed-native-call recovery, since
        // a throwing NewDev call may still have changed the active assignment.
        BoundAssignment assignment = await actions.BindAsync(stage, token);
        if (assignment is null)
            throw new InvalidDataException("M195_STEP8_BIND_NO_OBSERVATION");
        try
        {
            if (!assignment.NativeAccepted ||
                !string.Equals(assignment.PublishedInf, published,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(assignment.DriverVersion,
                    OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion,
                    StringComparison.Ordinal) ||
                assignment.ProblemCode is not (0 or 43))
                throw new InvalidDataException("M195_STEP8_BIND_ASSIGNMENT_NOT_VERIFIED");

            TrustEvidence trust = await actions.AuditAndRegisterOriginalAsync(
                assignment, token);
            if (trust is null || !ValidCoverage(trust) ||
                (trust.FullDualTrustVerified &&
                 (trust.OriginalFileCoverage != 14 ||
                  trust.LocalFileCoverage != 14 ||
                  trust.RegisteredOriginalCoverage != 14 ||
                  trust.RegisteredLocalCoverage != 14 ||
                  !trust.OriginalWhcpVerified)))
                throw new InvalidDataException("M195_STEP8_DUAL_AUDIT_RECEIPT_INVALID");

            // Represent *fully* verified dual trust only. A missing check is
            // evidence INCOMPLETE, not evidence that every AMD file failed.
            int full = trust.FullDualTrustVerified ? 14 : 0;
            var bound = new M195LifecycleSequence.BoundDriver(
                published, assignment.DriverVersion, signed.CatalogSha256,
                full, trust.OriginalWhcpVerified, true,
                assignment.RebootRequired);
            return new Outcome(bound, trust, true);
        }
        catch (Exception failure)
        {
            // The GPU has already been bound. Never omit rollback on a late
            // critical integrity failure, even if the 14x4 optional audit
            // itself is not a blanket install gate.
            try { await actions.RestoreAfterPostBindFailureAsync(assignment, failure); }
            catch (Exception recoveryError)
            {
                throw new AggregateException(
                    "M195_STEP8_FAILURE_AND_RECOVERY_EXCEPTION",
                    failure, recoveryError);
            }
            throw;
        }
    }

    private static bool ValidCoverage(TrustEvidence evidence) =>
        evidence.OriginalFileCoverage is >= 0 and <= 14 &&
        evidence.LocalFileCoverage is >= 0 and <= 14 &&
        evidence.RegisteredOriginalCoverage is >= 0 and <= 14 &&
        evidence.RegisteredLocalCoverage is >= 0 and <= 14;

    private static bool HasSha(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
