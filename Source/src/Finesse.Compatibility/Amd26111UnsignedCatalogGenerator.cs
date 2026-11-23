using System.Diagnostics;
using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Finesse.Compatibility;

/// <summary>
/// Source-driven offline catalog generation adapted from frozen M149.
/// Requires the exact pinned Microsoft WDK Inf2Cat executable and the staged
/// verified M195 194-file candidate. Generates an UNSIGNED .cat only.
/// Never creates/imports certificates, signs files, stages drivers, binds a GPU,
/// installs software or schedules a restart.
/// </summary>
public static class Amd26111UnsignedCatalogGenerator
{
    public const string PinnedInf2CatSha256 =
        "82C302FC9069783674B51665CE80E769F8500DB7FC737A4B8A3773C521950B86";

    public sealed record Result(string UnsignedCatalogPath, string Sha256,
        int VerifiedDriverFiles, bool CatalogContainsSigners,
        string StdoutLog, string StderrLog, int ExitCode);

    public static async Task<Result> GenerateAsync(
        AmdSourceFirstStager.UnsignedStageResult staged,
        Amd26111CandidateRecipe.VerifiedCandidate candidate,
        string verifiedInf2CatExe,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(candidate);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Inf2Cat requires Windows.");

        var original = staged.OriginalInventorySha256;
        var expected = Amd26111SourceInventory.PinnedCandidateManifestHash(candidate.Profile);
        if (original != Amd26111SourceInventory.PinnedOriginalManifestSha256 ||
            staged.CandidateInventorySha256 != expected || staged.VerifiedFiles != 194 ||
            staged.SignedOrInstallable)
            throw new InvalidDataException("M195_INF2CAT_UNVERIFIED_STAGE");

        string root = Path.GetFullPath(staged.PackageRoot);
        string originalCat = Path.GetFullPath(staged.PreservedOriginalCatalog);
        string tool = Path.GetFullPath(verifiedInf2CatExe);
        RejectReparseAncestors(root);
        RejectReparseAncestors(originalCat);
        RejectReparseAncestors(tool);
        if (!Directory.Exists(root) || !File.Exists(originalCat) ||
            !File.Exists(tool) || ShaFile(tool) != PinnedInf2CatSha256)
            throw new InvalidDataException("M195_INF2CAT_MISSING_OR_UNPINNED");
        if (ShaFile(originalCat) != staged.OriginalCatalogSha256)
            throw new InvalidDataException("M195_INF2CAT_ORIGINAL_AMD_CATALOG_CHANGED");

        string infPath = Path.Combine(root, "u0206030.inf");
        string datPath = Path.Combine(root, "B026778", "amdgcf.dat");
        if (!File.Exists(infPath) || !File.Exists(datPath) ||
            ShaFile(infPath) != candidate.InfSha256 ||
            ShaFile(datPath) != candidate.DatSha256)
            throw new InvalidDataException("M195_INF2CAT_INF_DAT_CHANGED");
        string target = Path.Combine(root, "u0206030.cat");
        if (File.Exists(target))
            throw new InvalidDataException("M195_INF2CAT_MAIN_CATALOG_ALREADY_EXISTS");

        // Manifest entry values for all 194 candidate files, including the
        // 192 unchanged originals. Do not trust a prepared folder only because
        // its two edited files match.
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        if (before.Length != 194)
            throw new InvalidDataException("M195_INF2CAT_FILE_COUNT_NOT_194");
        var beforeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in before)
        {
            token.ThrowIfCancellationRequested();
            RejectReparseAncestors(file);
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            Amd26111SourceInventory.ValidateRelative(relative);
            if (!beforeMap.TryAdd(relative, ShaFile(file)))
                throw new InvalidDataException("M195_INF2CAT_CASE_COLLISION");
        }
        if (!beforeMap.TryGetValue("u0206030.inf", out string? preInf) ||
            preInf != candidate.InfSha256 ||
            !beforeMap.TryGetValue("B026778/amdgcf.dat", out string? preDat) ||
            preDat != candidate.DatSha256)
            throw new InvalidDataException("M195_INF2CAT_INPUT_IDENTITY_MISMATCH");

        string evidenceDir = Path.GetDirectoryName(staged.EvidencePath) ??
            throw new InvalidDataException("M195_INF2CAT_NO_EVIDENCE_FOLDER");
        Directory.CreateDirectory(evidenceDir);
        string stdout = Path.Combine(evidenceDir, "M195-Inf2Cat-stdout.txt");
        string stderr = Path.Combine(evidenceDir, "M195-Inf2Cat-stderr.txt");
        if (File.Exists(stdout) || File.Exists(stderr))
            throw new IOException("M195_INF2CAT_LOGS_ALREADY_EXIST");
        var start = new ProcessStartInfo(tool)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(tool)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("/driver:" + root);
        start.ArgumentList.Add("/os:10_GE_X64");
        start.ArgumentList.Add("/verbose");

        using Process process = Process.Start(start) ??
            throw new IOException("M195_INF2CAT_COULD_NOT_START");
        string stdoutText;
        string stderrText;
        try
        {
            Task<string> o = process.StandardOutput.ReadToEndAsync(token);
            Task<string> e = process.StandardError.ReadToEndAsync(token);
            await Task.WhenAll(o, e, process.WaitForExitAsync(token));
            stdoutText = o.Result;
            stderrText = e.Result;
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await process.WaitForExitAsync(CancellationToken.None);
            }
            throw;
        }
        await File.WriteAllTextAsync(stdout, stdoutText, token);
        await File.WriteAllTextAsync(stderr, stderrText, token);
        if (process.ExitCode != 0)
            throw new InvalidDataException("M195_INF2CAT_FAILED_EXIT_" + process.ExitCode);
        if (!File.Exists(target) || new FileInfo(target).Length < 128)
            throw new InvalidDataException("M195_INF2CAT_CATALOG_MISSING");

        // A generated catalog is SignedData with ZERO SignerInfos. This is
        // NOT the live Finesse-signed catalog and is not installable.
        int count = CountCmsSigners(target);
        if (count != 0)
            throw new InvalidDataException("M195_INF2CAT_UNEXPECTEDLY_SIGNED");
        foreach ((string relative, string expectedHash) in beforeMap)
        {
            token.ThrowIfCancellationRequested();
            string file = Path.Combine(root,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file) || ShaFile(file) != expectedHash)
                throw new InvalidDataException("M195_INF2CAT_MODIFIED_CANDIDATE_FILE: " + relative);
        }
        var afterFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        if (afterFiles.Length != 195 ||
            afterFiles.Count(file => !beforeMap.ContainsKey(
                Path.GetRelativePath(root, file).Replace('\\', '/'))) != 1)
            throw new InvalidDataException("M195_INF2CAT_UNEXPECTED_FILE_MUTATION");
        if (ShaFile(originalCat) != staged.OriginalCatalogSha256)
            throw new InvalidDataException("M195_INF2CAT_ORIGINAL_CATALOG_MUTATED");

        return new Result(target, ShaFile(target), 194, false,
            stdout, stderr, process.ExitCode);
    }

    private static int CountCmsSigners(string path)
    {
        var cms = new AsnReader(File.ReadAllBytes(path), AsnEncodingRules.BER);
        var content = cms.ReadSequence();
        if (content.ReadObjectIdentifier() != "1.2.840.113549.1.7.2")
            throw new InvalidDataException("M195_INF2CAT_NOT_PKCS7_SIGNEDDATA");
        var wrapper = content.ReadSequence(new Asn1Tag(
            TagClass.ContextSpecific, 0, true));
        var signed = wrapper.ReadSequence();
        signed.ReadInteger();
        signed.ReadSetOf(skipSortOrderValidation: true);
        signed.ReadSequence();
        while (signed.HasData &&
               signed.PeekTag().TagClass == TagClass.ContextSpecific)
            signed.ReadEncodedValue();
        var signers = signed.ReadSetOf(skipSortOrderValidation: true);
        int count = 0;
        while (signers.HasData)
        {
            signers.ReadEncodedValue();
            count++;
        }
        signed.ThrowIfNotEmpty();
        wrapper.ThrowIfNotEmpty();
        content.ThrowIfNotEmpty();
        cms.ThrowIfNotEmpty();
        return count;
    }

    private static string ShaFile(string file)
    {
        using var stream = new FileStream(file, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RejectReparseAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_INF2CAT_REPARSE_PATH_REJECTED");
            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }
}
