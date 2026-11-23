namespace Finesse.Compatibility;

/// <summary>
/// Pure 14/14 dual-catalog evidence evaluator, derived from the exact
/// frozen M194 M191-Validation.ps1 counters. A copied CAT or correct file
/// hash is NOT automatically a registered catalog membership.
/// No Windows mutation or certificate changes.
/// </summary>
public static class M195DualCatalogCoverage
{
    public sealed record FileEvidence(
        string RelativePath, string Sha256,
        bool OriginalCatalogMemberVerified,
        bool LocalCatalogMemberVerified,
        bool RegisteredOriginalCatalogFound,
        bool RegisteredLocalCatalogFound);

    public sealed record Snapshot(
        bool DriverStoreInfMatches,
        bool DriverStoreLocalCatalogMatches,
        bool OriginalCatalogAuthenticodeWhcp,
        bool OriginalCatalogKernelPolicy,
        bool LocalCatalogPnPPolicy,
        IReadOnlyList<FileEvidence> Files);

    public sealed record Assessment(
        int OriginalCoverage,
        int LocalCoverage,
        int RegisteredOriginalCoverage,
        int RegisteredLocalCoverage,
        bool DriverStoreMappingVerified,
        bool DualTrustVerified,
        bool OriginalCatalogRegistrationSuggested,
        IReadOnlyList<string> MissingEvidence);

    public static Assessment Evaluate(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.Files);
        if (snapshot.Files.Count != 14)
            throw new InvalidDataException("M195_DUAL_TRUST_EXACTLY_14_TARGETS_REQUIRED");

        var expected = M195AmdDualTrustTargets.For26111.ToDictionary(
            t => t.Relative, t => t.OriginalSha256,
            StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int originals = 0, locals = 0, registeredOriginal = 0, registeredLocal = 0;
        foreach (FileEvidence file in snapshot.Files)
        {
            if (!seen.Add(file.RelativePath) ||
                !expected.TryGetValue(file.RelativePath, out string? hash) ||
                !string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "M195_DUAL_TRUST_UNEXPECTED_DUPLICATE_OR_MUTATED_MEMBER: " + file.RelativePath);
            if (file.OriginalCatalogMemberVerified) originals++;
            else errors.Add("ORIGINAL_CAT_MEMBER_MISSING:" + file.RelativePath);
            if (file.LocalCatalogMemberVerified) locals++;
            else errors.Add("LOCAL_CAT_MEMBER_MISSING:" + file.RelativePath);
            if (file.RegisteredOriginalCatalogFound) registeredOriginal++;
            else errors.Add("ORIGINAL_CATALOG_NOT_REGISTERED:" + file.RelativePath);
            if (file.RegisteredLocalCatalogFound) registeredLocal++;
            else errors.Add("LOCAL_CATALOG_NOT_REGISTERED:" + file.RelativePath);
        }
        if (seen.Count != expected.Count)
            throw new InvalidDataException("M195_DUAL_TRUST_INCOMPLETE_TARGET_IDENTITY");

        if (!snapshot.DriverStoreInfMatches) errors.Add("DRIVERSTORE_INF_HASH_NOT_MATCHED");
        if (!snapshot.DriverStoreLocalCatalogMatches) errors.Add("DRIVERSTORE_CATALOG_HASH_NOT_MATCHED");
        if (!snapshot.OriginalCatalogAuthenticodeWhcp) errors.Add("ORIGINAL_WHCP_IDENTITY_NOT_VERIFIED");
        if (!snapshot.OriginalCatalogKernelPolicy) errors.Add("ORIGINAL_CATALOG_KERNEL_POLICY_FAILED");
        if (!snapshot.LocalCatalogPnPPolicy) errors.Add("FINESSE_CATALOG_PNP_POLICY_FAILED");

        bool mapped = snapshot.DriverStoreInfMatches && snapshot.DriverStoreLocalCatalogMatches;
        bool success = mapped && snapshot.OriginalCatalogAuthenticodeWhcp &&
            snapshot.OriginalCatalogKernelPolicy && snapshot.LocalCatalogPnPPolicy &&
            originals == 14 && locals == 14 &&
            registeredOriginal == 14 && registeredLocal == 14;

        // M191 remediation applies only to a fully authenticated original
        // catalog that individually covers all 14 files. It does not
        // automatically mark registration or give it signing acceptance.
        bool registrationSuggested = mapped &&
            snapshot.OriginalCatalogAuthenticodeWhcp &&
            snapshot.OriginalCatalogKernelPolicy &&
            originals == 14 && registeredOriginal < 14;

        return new Assessment(originals, locals, registeredOriginal,
            registeredLocal, mapped, success, registrationSuggested,
            errors.AsReadOnly());
    }
}
