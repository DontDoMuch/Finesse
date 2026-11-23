using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// M195 two-phase handoff after AMD's original installer was authenticated and
/// extracted by the WINDOWS acquisition layer. Copies the exact 194 source
/// files and replaces only INF/DAT with independently verified candidate bytes.
/// Does NOT sign, bind, install, register catalogs, or change Windows trust.
/// </summary>
public static class AmdSourceFirstStager
{
    public sealed record UnsignedStageResult(
        string PackageRoot,
        string PreservedOriginalCatalog,
        string OriginalCatalogSha256,
        string OriginalInventorySha256,
        string CandidateInventorySha256,
        int VerifiedFiles,
        string EvidencePath,
        bool SignedOrInstallable);

    /// <summary>
    /// Production entry point: BOTH source and output manifest identities are
    /// pinned to the frozen 26.11.1 M194 closure. No ad-hoc manifest accepted.
    /// </summary>
    public static Task<UnsignedStageResult> StagePinned26111Async(
        string extractedOfficialAmdDisplayRoot,
        string freshOutputDirectory,
        string original194ManifestText,
        Amd26111CandidateRecipe.VerifiedCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        AmdVerifiedSourceInventory original =
            Amd26111SourceInventory.ParsePinned26111(original194ManifestText);
        string exactCandidateManifest = Amd26111SourceInventory.PinnedCandidateManifestHash(
            candidate.Profile);
        return StageAsync(extractedOfficialAmdDisplayRoot, freshOutputDirectory,
            original, candidate, exactCandidateManifest, cancellationToken);
    }

    /// <summary>
    /// Explicitly pinned offline staging primitive, used by the production
    /// wrapper and by vendor-free synthetic regression tests.
    /// </summary>
    public static async Task<UnsignedStageResult> StageAsync(
        string extractedSourceRoot,
        string freshOutputDirectory,
        AmdVerifiedSourceInventory original,
        Amd26111CandidateRecipe.VerifiedCandidate candidate,
        string expectedCandidateManifestSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidate);
        if (original.Entries.Count != 194)
            throw new InvalidDataException("AMD_STAGE_NOT_194_SOURCE_FILES");
        string source = Path.GetFullPath(extractedSourceRoot);
        string output = Path.GetFullPath(freshOutputDirectory);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException("Original AMD extraction root is absent.");
        if (Directory.Exists(output) || File.Exists(output))
            throw new IOException("Stage output must not exist; refusing overwrite.");
        if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AMD_STAGE_OUTPUT_INSIDE_SOURCE");
        if (Directory.GetParent(output) is not { } parent)
            throw new InvalidDataException("AMD_STAGE_INVALID_OUTPUT_PARENT");
        CheckNoReparse(source);
        CheckNoReparse(parent.FullName);

        string infSha = Sha(candidate.InfBytes);
        string datSha = Sha(candidate.DatBytes);
        if (infSha != candidate.InfSha256 || datSha != candidate.DatSha256)
            throw new InvalidDataException("AMD_STAGE_CANDIDATE_BYTES_DO_NOT_MATCH_HASH");
        string computedCandidateManifest =
            Amd26111SourceInventory.CandidateManifestSha256(original, candidate);
        if (!string.Equals(computedCandidateManifest, expectedCandidateManifestSha256,
                StringComparison.Ordinal))
            throw new InvalidDataException("AMD_STAGE_CANDIDATE_MANIFEST_NOT_APPROVED");

        // Validation first. Nothing is written until every original source file
        // has an exact expected hash (including unchanged kernel/user binaries).
        foreach (AmdSourceFileProof entry in original.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = ResolveFile(source, entry.Relative);
            if (!File.Exists(path) || ShaFile(path) != entry.Sha256)
                throw new InvalidDataException("AMD_STAGE_ORIGINAL_FILE_SHA_MISMATCH: " + entry.Relative);
        }
        const string originalCatName = "u0206030.cat";
        string originalCat = ResolveFile(source, originalCatName);
        if (!File.Exists(originalCat))
            throw new FileNotFoundException("Original Microsoft-signed AMD catalog is missing.", originalCat);
        // No trust claim here: catalog signature must be checked by Windows
        // WinVerifyTrust and 14/14 original coverage in the later lifecycle.
        string catalogSha = ShaFile(originalCat);

        Directory.CreateDirectory(parent.FullName);
        string temp = Path.Combine(parent.FullName, ".finesse-m195-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            string package = Path.Combine(temp, "PreparedDriver");
            Directory.CreateDirectory(package);
            foreach (AmdSourceFileProof entry in original.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = ResolveFutureFile(package, entry.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string expected = entry.Sha256;
                if (entry.Relative.Equals("u0206030.inf", StringComparison.OrdinalIgnoreCase))
                {
                    expected = candidate.InfSha256;
                    await File.WriteAllBytesAsync(destination, candidate.InfBytes, cancellationToken);
                }
                else if (entry.Relative.Equals("B026778/amdgcf.dat", StringComparison.OrdinalIgnoreCase))
                {
                    expected = candidate.DatSha256;
                    await File.WriteAllBytesAsync(destination, candidate.DatBytes, cancellationToken);
                }
                else
                {
                    string origin = ResolveFile(source, entry.Relative);
                    await using var input = new FileStream(origin, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await using var dest = new FileStream(destination, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    await input.CopyToAsync(dest, cancellationToken);
                    await dest.FlushAsync(cancellationToken);
                }
                if (ShaFile(destination) != expected)
                    throw new InvalidDataException("AMD_STAGE_OUTPUT_SHA_MISMATCH: " + entry.Relative);
            }
            int files = Directory.EnumerateFiles(package, "*",
                SearchOption.AllDirectories).Count();
            if (files != 194 || File.Exists(Path.Combine(package, originalCatName)))
                throw new InvalidDataException("AMD_STAGE_PACKAGE_CLOSURE_NOT_194");

            string originalDirectory = Path.Combine(temp, "OriginalAMD");
            Directory.CreateDirectory(originalDirectory);
            string catalogCopy = Path.Combine(originalDirectory, originalCatName);
            File.Copy(originalCat, catalogCopy);
            if (ShaFile(catalogCopy) != catalogSha)
                throw new InvalidDataException("AMD_STAGE_PRESERVED_CATALOG_MISMATCH");

            string evidenceName = "M195-UnsignedSourceHandoff.json";
            var evidence = new
            {
                Schema = "Finesse.M195.OriginalSourceToUnsignedCandidate.v1",
                Status = "OFFLINE_UNSIGNED_NOT_INSTALLABLE",
                Profile = candidate.Profile.Id,
                OriginalInventorySha256 = original.CanonicalSha256,
                CandidateInventorySha256 = computedCandidateManifest,
                OriginalAmdCatalogSha256 = catalogSha,
                OriginalCatalogSignatureVerified = false,
                CandidateCatalogSigned = false,
                CatalogDualCoverageVerified = false,
                FilesVerified = files,
                SigningAttempted = false,
                DriverInstallAttempted = false,
                WindowsTrustModified = false,
                RestartScheduled = false
            };
            string evidencePath = Path.Combine(temp, evidenceName);
            await File.WriteAllTextAsync(evidencePath,
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), cancellationToken);
            // Rename only after all 194 hashes and original catalog copy pass.
            Directory.Move(temp, output);
            return new UnsignedStageResult(
                Path.Combine(output, "PreparedDriver"),
                Path.Combine(output, "OriginalAMD", originalCatName),
                catalogSha, original.CanonicalSha256,
                computedCandidateManifest, files,
                Path.Combine(output, evidenceName), false);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string ShaFile(string path)
    {
        CheckNoReparse(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ResolveFile(string root, string relative)
    {
        string result = ResolveFutureFile(root, relative);
        CheckNoReparse(result);
        return result;
    }

    private static string ResolveFutureFile(string root, string relative)
    {
        Amd26111SourceInventory.ValidateRelative(relative);
        string fullRoot = Path.GetFullPath(root);
        string full = Path.GetFullPath(Path.Combine(fullRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AMD_STAGE_PATH_ESCAPED_ROOT");
        return full;
    }

    private static void CheckNoReparse(string path)
    {
        string full = Path.GetFullPath(path);
        string? cursor = File.Exists(full) || Directory.Exists(full)
            ? full : Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(cursor))
        {
            if (File.Exists(cursor) || Directory.Exists(cursor))
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("AMD_STAGE_REPARSE_POINT_REJECTED");
            }
            string? parent = Path.GetDirectoryName(cursor);
            if (string.Equals(parent, cursor, StringComparison.OrdinalIgnoreCase))
                break;
            cursor = parent;
        }
    }
}
