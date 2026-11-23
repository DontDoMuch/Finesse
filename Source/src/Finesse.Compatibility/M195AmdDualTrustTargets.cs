namespace Finesse.Compatibility;

/// <summary>
/// Fourteen byte-identical original AMD 26.11.1 graphics targets from frozen
/// M115/M191 evidence. Paths and SHA-256 hashes only; NO vendor file bytes.
/// These are the members that must be covered by both the authentic Microsoft
/// AMD catalog and the locally signed candidate catalog.
/// </summary>
public static class M195AmdDualTrustTargets
{
    public sealed record CriticalTarget(string Relative, string OriginalSha256);

    public static IReadOnlyList<CriticalTarget> For26111 { get; } =
        Array.AsReadOnly(new[]
        {
            new CriticalTarget("B026778/amdkmdag.sys",
                "34B18AB97C75010F91128F5B802C9D8A3CFC6B9DAE586A8B42FD4A00738EB04D"),
            new CriticalTarget("B026778/amdxc32.dll",
                "57925EE7EC45E80F3F5B790CD8D9E659BE5CE6E3644D564576B18681EA3372EB"),
            new CriticalTarget("B026778/amdxc64.dll",
                "5090826B251CD75CFB4F8583EE744089382B5C88CE62D78A46128AED0B8CF33B"),
            new CriticalTarget("B026778/amdxcffx64.dll",
                "95176889A16DFDC1F1DFF4C3C535301065F2DC1761519A0C92FE2067EDBC6218"),
            new CriticalTarget("B026778/amdxcstub32.dll",
                "4BE84D867B1C8498B0EE87766CCCC418AA0AB23781EB8760D3E7CE3FAEF526ED"),
            new CriticalTarget("B026778/amdxcstub64.dll",
                "A1ECD9EBB80051466506FA6157E9F7BF3EEF47E84D87E17733CED88931527929"),
            new CriticalTarget("B026778/amdxn32.dll",
                "E380026DC2B1D0F8D062DF73BB00461B444DA41C783FFED6CD10864B31162C4C"),
            new CriticalTarget("B026778/amdxn64.dll",
                "4B549B0D6797B06B8FA6320A12AD4A3FCDA190F8051D43E86DCC234D654E2D18"),
            new CriticalTarget("B026778/amdxx32.dll",
                "EBC9E0E94D1CD6B381273D393711174126E342635A2408494F4462178AB2B5BD"),
            new CriticalTarget("B026778/amdxx64.dll",
                "FEBA81C1AEB68C56FBB1E7259BA00575041046BC2D14E4301DCF6CEAF1B4ED57"),
            new CriticalTarget("B026778/atidx9loader32.dll",
                "12A3258AF62169B12B7B527A6416EB7067D6347DF1B72C34426FB70A2737E81D"),
            new CriticalTarget("B026778/atidx9loader64.dll",
                "45D55B3E40940567FE240F37EBB67A47C71DDD712A8C57FDD5002556B3E38F2A"),
            new CriticalTarget("B026778/atidxxstub32.dll",
                "D5778A0C93DA7499EA11C731DB719FCD21E0A8E3AC6E9721059CC9310F4BAF52"),
            new CriticalTarget("B026778/atidxxstub64.dll",
                "B15E0AF75AC379D5C723F1FE46C4E8A7FA01D5C5AA7E922A708914D62311B804")
        });

    public static void VerifySourceInventory(
        AmdVerifiedSourceInventory original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.CanonicalSha256 !=
            Amd26111SourceInventory.PinnedOriginalManifestSha256 ||
            For26111.Count != 14 ||
            For26111.Select(x => x.Relative).Distinct(
                StringComparer.OrdinalIgnoreCase).Count() != 14)
            throw new InvalidDataException("M195_14_TARGETS_OR_SOURCE_MANIFEST_INVALID");
        var files = original.Entries.ToDictionary(
            entry => entry.Relative, entry => entry.Sha256,
            StringComparer.OrdinalIgnoreCase);
        foreach (CriticalTarget target in For26111)
            if (!files.TryGetValue(target.Relative, out string? sha) ||
                !string.Equals(sha, target.OriginalSha256, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "M195_ORIGINAL_GRAPHICS_14_MEMBER_MISMATCH: " + target.Relative);
    }
}
