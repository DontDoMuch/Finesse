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
        "8776BD900A0B669D03F8DF308B9BB5F4B323DE90C5E699D8837765AD719D7C64";

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
            "LegionGo1" => "00A7F9DF1BC365C4D996100D70EE85B1E945C8BA44753A8B008C00F7184298E7",
            "LegionGoS-Z1Extreme" => "FB4647C4EFB8F8FCCD237938B68CA4C93D15182BE05BCB2875ABFE1F20F1098F",
            "LegionGo2-Z2Extreme" => "0C08A5A88A17CFF1C62AC0A8B035F67F78DD2B11F861512B13E8970F965EE17A",
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
