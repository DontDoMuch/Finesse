using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Finesse.Compatibility;

/// <summary>
/// Verified transition from the frozen M194 offline unsigned catalog to the
/// live M179 signing transaction. This API is READ-ONLY: it does not create
/// signer trust, sign, stage, bind, or modify Windows. The future single live
/// signer must consume a validated preflight, not an arbitrary CAT path.
/// </summary>
public static class M195LiveSigningPreflight
{
    public const string PinnedMicrosoftSignToolSha256 =
        "80972965E7FC311D293222B1A0E2C1BFB60F363239173964DBE2A71638314B9F";
    public const string PinnedOriginalAmdMainCatalogSha256 =
        "786D0B7F596D367F2C71BADAA31068DD1A63C20E0C8C7DE94E8E23019EBAFE04";

    public sealed record Verified(
        string PreparedInf, string PreparedDat, string UnsignedMainCatalog,
        string OriginalAmdCatalog, string PinnedSignTool,
        string PackageRoot, string EvidenceDirectory,
        string UnsignedCatalogSha256, string CandidateInventorySha256,
        IReadOnlyDictionary<string, string> ExpectedMemberHashes);

    public static Verified Validate(
        AmdSourceFirstStager.UnsignedStageResult staged,
        Amd26111CandidateRecipe.VerifiedCandidate candidate,
        Amd26111UnsignedCatalogGenerator.Result unsigned,
        string original194ManifestText,
        string signToolExe)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(unsigned);
        ArgumentNullException.ThrowIfNull(original194ManifestText);
        ArgumentException.ThrowIfNullOrWhiteSpace(signToolExe);

        AmdVerifiedSourceInventory originals =
            Amd26111SourceInventory.ParsePinned26111(original194ManifestText);
        string candidateManifest =
            Amd26111SourceInventory.PinnedCandidateManifestHash(candidate.Profile);
        if (staged.VerifiedFiles != 194 || staged.SignedOrInstallable ||
            unsigned.VerifiedDriverFiles != 194 || unsigned.CatalogContainsSigners ||
            unsigned.ExitCode != 0 ||
            staged.OriginalInventorySha256 != originals.CanonicalSha256 ||
            staged.CandidateInventorySha256 != candidateManifest ||
            Amd26111SourceInventory.CandidateManifestSha256(
                originals, candidate) != candidateManifest)
            throw new InvalidDataException("M195_LIVE_SIGN_INVALID_SOURCE_OR_CANDIDATE_RECEIPT");

        string root = Path.GetFullPath(staged.PackageRoot);
        string inf = Path.Combine(root, "u0206030.inf");
        string dat = Path.Combine(root, "B026778", "amdgcf.dat");
        string catalog = Path.Combine(root, "u0206030.cat");
        string originalCatalog = Path.GetFullPath(staged.PreservedOriginalCatalog);
        string signTool = Path.GetFullPath(signToolExe);
        string evidence = Path.GetDirectoryName(Path.GetFullPath(staged.EvidencePath)) ??
            throw new InvalidDataException("M195_LIVE_SIGN_NO_EVIDENCE_DIRECTORY");

        foreach (string path in new[] {
            root, inf, dat, catalog, originalCatalog, signTool, evidence })
            RejectReparseAncestor(path);
        if (!Directory.Exists(root) || !Directory.Exists(evidence) ||
            !File.Exists(inf) || !File.Exists(dat) ||
            !File.Exists(catalog) || !File.Exists(originalCatalog) ||
            !File.Exists(signTool) ||
            !string.Equals(catalog, Path.GetFullPath(unsigned.UnsignedCatalogPath),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_LIVE_SIGN_FILE_IDENTITY_MISMATCH");
        if (HashFile(signTool) != PinnedMicrosoftSignToolSha256)
            throw new InvalidDataException("M195_LIVE_SIGN_MICROSOFT_TOOL_SHA_MISMATCH");
        if (HashFile(originalCatalog) != PinnedOriginalAmdMainCatalogSha256 ||
            HashFile(originalCatalog) != staged.OriginalCatalogSha256)
            throw new InvalidDataException("M195_LIVE_SIGN_ORIGINAL_AMD_CATALOG_CHANGED");
        if (HashFile(catalog) != unsigned.Sha256 ||
            HashFile(inf) != candidate.InfSha256 ||
            HashFile(dat) != candidate.DatSha256 ||
            Convert.ToHexString(SHA256.HashData(candidate.InfBytes)) != candidate.InfSha256 ||
            Convert.ToHexString(SHA256.HashData(candidate.DatBytes)) != candidate.DatSha256)
            throw new InvalidDataException("M195_LIVE_SIGN_INF_DAT_OR_CAT_SHA_MISMATCH");

        // Full source-driven package proof, not just INF, DAT, and CAT.
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (AmdSourceFileProof proof in originals.Entries)
        {
            string sha = proof.Relative.Equals("u0206030.inf",
                StringComparison.OrdinalIgnoreCase) ? candidate.InfSha256 :
                proof.Relative.Equals("B026778/amdgcf.dat",
                StringComparison.OrdinalIgnoreCase) ? candidate.DatSha256 : proof.Sha256;
            if (!expected.TryAdd(proof.Relative, sha))
                throw new InvalidDataException("M195_LIVE_SIGN_DUPLICATE_FILE_CONTRACT");
        }
        IReadOnlyDictionary<string, string> fullExpected =
            new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(expected, StringComparer.OrdinalIgnoreCase));
        string[] sourceFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        if (sourceFiles.Length != 195)
            throw new InvalidDataException("M195_LIVE_SIGN_EXPECTED_194_PLUS_UNSIGNED_CAT");
        foreach (string file in sourceFiles)
        {
            RejectReparseAncestor(file);
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            Amd26111SourceInventory.ValidateRelative(relative);
            if (relative.Equals("u0206030.cat", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!expected.Remove(relative, out string? hash) || HashFile(file) != hash)
                throw new InvalidDataException("M195_LIVE_SIGN_SOURCE_FILE_CHANGED: " + relative);
        }
        if (expected.Count != 0)
            throw new InvalidDataException("M195_LIVE_SIGN_EXPECTED_FILES_MISSING");

        string[] ownedProofs = [
            "M195-Live-Signer-Proof.json",
            "M195-Live-Signer-Console.txt",
            "M195-Live-Signer-Script.ps1"
        ];
        if (ownedProofs.Any(name => File.Exists(Path.Combine(evidence, name))))
            throw new InvalidDataException("M195_LIVE_SIGN_TRANSACTION_ALREADY_USED");

        return new Verified(inf, dat, catalog, originalCatalog, signTool,
            root, evidence, unsigned.Sha256, candidateManifest, fullExpected);
    }

    public static bool IsOwnedSigner(string? thumbprint, string? subject) =>
        !string.IsNullOrWhiteSpace(thumbprint) &&
        thumbprint.Length == 40 && thumbprint.All(Uri.IsHexDigit) &&
        !string.IsNullOrWhiteSpace(subject) &&
        Regex.IsMatch(subject,
            @"\ACN=Finesse-Local-Live-[0-9a-fA-F]{32}\z",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RejectReparseAncestor(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_LIVE_SIGN_REPARSE_POINT");
            string? parent = Path.GetDirectoryName(cursor);
            if (string.Equals(cursor, parent, StringComparison.OrdinalIgnoreCase))
                break;
            cursor = parent;
        }
    }
}
