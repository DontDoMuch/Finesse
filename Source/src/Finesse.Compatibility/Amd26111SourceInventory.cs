using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Finesse.Compatibility;

/// <summary>
/// Original AMD extraction inventory, represented ONLY as relative names and
/// SHA-256 proofs. No AMD or Lenovo file content is embedded.
/// </summary>
public sealed record AmdSourceFileProof(string Relative, string Sha256);

public sealed record AmdVerifiedSourceInventory(
    IReadOnlyList<AmdSourceFileProof> Entries,
    string CanonicalSha256);

public static class Amd26111SourceInventory
{
    // Calculated from frozen M194 194-file inventory, with the modified Go1
    // INF SHA replaced by the authentic original AMD INF SHA. DAT remains
    // original. Canonical form is ordinal-case-insensitive path order, each
    // line SHA256 + two spaces + exact relative path + LF, including last LF.
    // This is a fingerprint of file NAMES/HASHES, not any vendor file bytes.
    public const string PinnedOriginalManifestSha256 =
        "C7757CFE6D743D7A6AAC33292753FEB08811CA898A43BF60B724481E24042D08";

    public static AmdVerifiedSourceInventory ParsePinned26111(string manifestText) =>
        ParseAndVerify(manifestText,
            OfficialAmd26111SourceContract.VerifiedFrozenM194,
            PinnedOriginalManifestSha256);

    /// <summary>
    /// Generic offline manifest verifier for tests and other explicitly pinned
    /// source contracts. Does NOT grant approval to install a driver.
    /// </summary>
    public static AmdVerifiedSourceInventory ParseAndVerify(
        string manifestText, OfficialAmd26111SourceContract contract,
        string expectedCanonicalSha256)
    {
        ArgumentNullException.ThrowIfNull(manifestText);
        ArgumentNullException.ThrowIfNull(contract);
        RequireHash(expectedCanonicalSha256);
        if (manifestText.Length > 200_000)
            throw new InvalidDataException("AMD_SOURCE_MANIFEST_TOO_LARGE");

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<AmdSourceFileProof>();
        foreach (string line in manifestText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.Length == 0)
                continue;
            if (line.Length < 68 || line[64] != ' ' || line[65] != ' ')
                throw new InvalidDataException("AMD_SOURCE_MANIFEST_ROW_MALFORMED");
            string sha = line[..64].ToUpperInvariant();
            RequireHash(sha);
            string relative = line[66..];
            ValidateRelative(relative);
            if (!unique.Add(relative))
                throw new InvalidDataException("AMD_SOURCE_MANIFEST_DUPLICATE");
            entries.Add(new AmdSourceFileProof(relative, sha));
        }
        if (entries.Count != contract.SelectedFileCount || entries.Count != 194)
            throw new InvalidDataException("AMD_SOURCE_MANIFEST_NOT_194");

        static string RequireSingle(List<AmdSourceFileProof> files, string path)
        {
            var matches = files.Where(x => string.Equals(x.Relative, path,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("AMD_SOURCE_REQUIRED_FILE_NOT_EXACT: " + path);
            return matches[0].Sha256;
        }
        if (RequireSingle(entries, contract.OriginalInfRelative) != contract.OriginalInfSha256 ||
            RequireSingle(entries, contract.OriginalDatRelative) != contract.OriginalDatSha256 ||
            RequireSingle(entries, contract.OriginalKernelRelative) != contract.OriginalKernelSha256)
            throw new InvalidDataException("AMD_SOURCE_PINNED_CORE_HASH_MISMATCH");

        if (entries.Any(x => string.Equals(x.Relative, contract.OriginalMainCatalogRelative,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("AMD_SOURCE_MAIN_CATALOG_MUST_BE_SEPARATE");

        string normalized = string.Concat(entries
            .OrderBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Sha256 + "  " + x.Relative + "\n"));
        string actual = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));
        if (!string.Equals(actual, expectedCanonicalSha256, StringComparison.Ordinal))
            throw new InvalidDataException("AMD_SOURCE_FULL_MANIFEST_HASH_MISMATCH");
        return new AmdVerifiedSourceInventory(entries.AsReadOnly(), actual);
    }

    public static string CandidateManifestSha256(
        AmdVerifiedSourceInventory inventory,
        Amd26111CandidateRecipe.VerifiedCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(candidate);
        string? infHash = null, datHash = null;
        var lines = new List<AmdSourceFileProof>();
        foreach (AmdSourceFileProof item in inventory.Entries)
        {
            string replacement = item.Sha256;
            if (string.Equals(item.Relative, "u0206030.inf", StringComparison.OrdinalIgnoreCase))
            {
                infHash = candidate.InfSha256;
                replacement = infHash;
            }
            else if (string.Equals(item.Relative, "B026778/amdgcf.dat", StringComparison.OrdinalIgnoreCase))
            {
                datHash = candidate.DatSha256;
                replacement = datHash;
            }
            RequireHash(replacement);
            lines.Add(new AmdSourceFileProof(item.Relative, replacement));
        }
        if (infHash is null || datHash is null)
            throw new InvalidDataException("AMD_CANDIDATE_REPLACEMENTS_ABSENT");

        string canonical = string.Concat(lines
            .OrderBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Sha256 + "  " + x.Relative + "\n"));
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(canonical)));
    }

    public static string PinnedCandidateManifestHash(HandheldProfile profile) =>
        profile.Id switch
        {
            "LegionGo1" => "6F2533085DF9656AE168AF7D95D490E333B338D1D9181DD3C8B32A24A90A7944",
            "LegionGoS-Z1Extreme" => "D81432A1469DC04FFCCAE072EBE0E3B064A6DFF9E9949EFF92FA035ED8A49F1B",
            "LegionGo2-Z2Extreme" => "B5F1C78B9B929ADCE7C5BDDD54CD3A2FA03911A2117159E63266D67CA7B7E369",
            _ => throw new InvalidDataException("AMD_CANDIDATE_UNSUPPORTED_PROFILE")
        };

    public static void ValidateRelative(string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Length > 512 ||
            relative.StartsWith("/", StringComparison.Ordinal) ||
            relative.Contains('\\') || relative.Contains(':') ||
            relative.Contains("//", StringComparison.Ordinal) ||
            relative.Any(ch => ch < 32 || ch > 126))
            throw new InvalidDataException("AMD_SOURCE_INVALID_RELATIVE_PATH");

        foreach (string segment in relative.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == ".." ||
                segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.Contains('*') || segment.Contains('?') ||
                segment.Contains('"') || segment.Contains('<') ||
                segment.Contains('>') || segment.Contains('|') ||
                Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidDataException("AMD_SOURCE_INVALID_PATH_SEGMENT");
        }
    }

    private static void RequireHash(string sha)
    {
        if (sha.Length != 64 || sha.Any(ch => !Uri.IsHexDigit(ch)))
            throw new InvalidDataException("AMD_SOURCE_INVALID_SHA256");
    }
}
