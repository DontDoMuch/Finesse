namespace Finesse.Compatibility;

/// <summary>
/// Public 26.11.1 candidate construction boundary. Consumes external,
/// byte-verified original AMD INF/DAT plus an independently supplied, pinned
/// Lenovo policy. Does not acquire, embed, redistribute, sign, stage or install
/// any proprietary files.
/// </summary>
public static class Amd26111CandidateRecipe
{
    public const string ReleaseMarker =
        "26.20.15.16-261109a-206030E-AMD-Software-Adrenalin-Edition";
    public const string OriginalDatSha256 =
        "2DC60D0B41488E504E78213B8DA1DF790E7E39AF67A892E9441F41642659E459";

    public sealed record VerifiedCandidate(
        HandheldProfile Profile,
        byte[] InfBytes,
        byte[] DatBytes,
        string InfSha256,
        string DatSha256,
        string ReleaseMarker);

    /// <summary>
    /// Atomic in-memory construction: no filesystem or Windows mutation.
    /// The output is an UNSIGNED package component pair. It must never be
    /// passed directly to pnputil or confused with a signed driver package.
    /// </summary>
    public static VerifiedCandidate Build(
        byte[] originalOfficialInf,
        byte[] originalOfficialDat,
        string exactLenovoPolicyRows,
        HandheldProfile profile)
    {
        ArgumentNullException.ThrowIfNull(originalOfficialInf);
        ArgumentNullException.ThrowIfNull(originalOfficialDat);
        ArgumentNullException.ThrowIfNull(exactLenovoPolicyRows);
        ArgumentNullException.ThrowIfNull(profile);

        string sourceDatHash = AmdInfSourceRebuilder.Sha256(originalOfficialDat);
        if (sourceDatHash != OriginalDatSha256)
            throw new InvalidDataException("AMD26111_ORIGINAL_DAT_HASH_MISMATCH");

        // Fail before any external mutation: all three inputs must reproduce
        // frozen M194 byte-for-byte. Policy rows are validated by the target
        // INF SHA, not merely by their count.
        AmdInfSourceRebuilder.InfResult inf =
            AmdInfSourceRebuilder.RebuildPinned26111(
                originalOfficialInf, profile, exactLenovoPolicyRows);

        byte[] dat = AmdDatReleaseRebuilder.BuildForVerifiedProfile(
            originalOfficialDat, ReleaseMarker, profile);
        string datSha = AmdInfSourceRebuilder.Sha256(dat);
        string expectedDatSha = profile.Id switch
        {
            "LegionGo1" or "LegionGoS-Z1Extreme" =>
                "7986B5DC0CB3C2207ECE857D0060475A603B5342649D146874850C0D4A2509CA",
            "LegionGo2-Z2Extreme" =>
                "B63E4F58C164950B41C07071B3CF6797B35ACC597B9476836FCA66E8A658E3FD",
            _ => throw new InvalidDataException("AMD26111_UNSUPPORTED_M194_PROFILE")
        };
        if (datSha != expectedDatSha)
            throw new InvalidDataException("AMD26111_FROZEN_DAT_SHA256_MISMATCH");

        return new VerifiedCandidate(
            profile, inf.CleanedInf, dat, inf.CleanedInfSha256,
            datSha, ReleaseMarker);
    }
}
