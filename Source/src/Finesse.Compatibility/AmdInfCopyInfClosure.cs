namespace Finesse.Compatibility;

/// <summary>
/// Caller-supplied, release-specific evidence that an INF directive references
/// a companion package absent from the authenticated AMD source.
/// The library does NOT assume all AMD versions require the same removals.
/// </summary>
public sealed record VerifiedCopyInfRemoval(
    string ExactDirectiveWithCrLf,
    int ExpectedOccurrences);

public static class AmdInfCopyInfClosure
{
    /// <summary>
    /// Applies only the explicitly approved CopyINF removals. Never reconstructs
    /// a proprietary INF by itself and never claims a new catalog is signed.
    /// A new catalog MUST be created after any modification.
    /// </summary>
    public static string RemoveVerifiedDependencies(
        string originalInf,
        HandheldProfile profile,
        IReadOnlyList<VerifiedCopyInfRemoval> approvedRemovals)
    {
        ArgumentNullException.ThrowIfNull(originalInf);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(approvedRemovals);
        if (string.IsNullOrEmpty(originalInf) ||
            !originalInf.Contains("[" + profile.InstallSection + "]", StringComparison.Ordinal) ||
            !originalInf.Contains(profile.HardwareId, StringComparison.Ordinal))
            throw new InvalidDataException("AMD_INF_PROFILE_IDENTITY_MISSING");

        string policyMarker = "LegionGo_Finesse_" +
            (profile.Id == HandheldProfiles.Go1.Id ? "Go1" :
             profile.Id == HandheldProfiles.GoS.Id ? "GoS" :
             profile.Id == HandheldProfiles.Go2.Id ? "Go2" :
             throw new InvalidDataException("AMD_INF_UNSUPPORTED_PROFILE")) + "_Policy";

        if (!originalInf.Contains(policyMarker, StringComparison.Ordinal))
            throw new InvalidDataException("AMD_INF_LENOVO_POLICY_IDENTITY_MISSING");

        if (approvedRemovals.Count == 0)
            return originalInf; // Verified no removals; do NOT apply 26.11.1 by default.

        var unique = new HashSet<string>(StringComparer.Ordinal);
        string result = originalInf;
        foreach (VerifiedCopyInfRemoval approved in approvedRemovals)
        {
            string line = approved.ExactDirectiveWithCrLf;
            if (string.IsNullOrWhiteSpace(line) ||
                !line.EndsWith("\r\n", StringComparison.Ordinal) ||
                !line.TrimStart().StartsWith("CopyINF", StringComparison.OrdinalIgnoreCase) ||
                !unique.Add(line) ||
                approved.ExpectedOccurrences < 1)
                throw new InvalidDataException("AMD_INF_INVALID_VERIFIED_COPYINF_CONTRACT");
            int actual = result.Split([line], StringSplitOptions.None).Length - 1;
            if (actual != approved.ExpectedOccurrences)
                throw new InvalidDataException(
                    "AMD_INF_COPYINF_COUNT_MISMATCH: " + actual + "/" + approved.ExpectedOccurrences);
            result = result.Replace(line, "", StringComparison.Ordinal);
            if (result.Contains(line, StringComparison.Ordinal))
                throw new InvalidDataException("AMD_INF_COPYINF_REMOVAL_INCOMPLETE");
        }
        return result;
    }
}
