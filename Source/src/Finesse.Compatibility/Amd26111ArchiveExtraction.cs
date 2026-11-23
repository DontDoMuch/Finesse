using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Original AMD 26.11.1 selective extraction: exact signed AMD EXE and pinned
/// 7zr 26.04 binary, selected 194 original source files + original AMD CAT.
/// No vendor bytes, credentials, SDK binaries or certificates are embedded.
/// Does NOT sign, stage, bind or install a GPU package.
/// </summary>
public static class Amd26111ArchiveExtraction
{
    public const string Pinned7zrSha256 =
        "256FECA8E274E5DA655E2A284FABAFD9F554365EB164862089DACD4E8276D282";
    public const long Pinned7zrBytes = 602_624;
    private const long MinimumFreeBytes = 8_000_000_000L;

    public sealed record VerifiedExtraction(
        string DisplayRoot,
        string Workspace,
        string SourceManifestSha256,
        string OriginalCatalogSha256,
        int OriginalFilesVerified,
        int ExtractorExit,
        string EvidenceFile);

    public static async Task<VerifiedExtraction> ExtractAsync(
        string exactOfficialAmdInstaller,
        string original7zrExecutable,
        string newWorkspace,
        string publicOriginal194Manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(publicOriginal194Manifest);
        AmdVerifiedSourceInventory inventory =
            Amd26111SourceInventory.ParsePinned26111(publicOriginal194Manifest);
        OfficialAmdInstallerVerifier.VerifiedSource verified =
            await OfficialAmdInstallerVerifier.Verify26111Async(
                exactOfficialAmdInstaller, cancellationToken);

        string extractor = Path.GetFullPath(original7zrExecutable);
        if (!string.Equals(Path.GetFileName(extractor), "7zr.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_UNAPPROVED_EXTRACTOR_NAME");
        RequireNoReparseAncestors(extractor);
        if (!File.Exists(extractor) ||
            new FileInfo(extractor).Length != Pinned7zrBytes ||
            await HashFileAsync(extractor, cancellationToken) != Pinned7zrSha256)
            throw new InvalidDataException("M195_7ZR_PINNED_BYTES_FAILED");

        string workspace = Path.GetFullPath(newWorkspace);
        if (Directory.Exists(workspace) || File.Exists(workspace))
            throw new IOException("M195_WORKSPACE_ALREADY_EXISTS");
        // The fresh workspace may share a parent folder with the official
        // installer or tool (for example Downloads/Finesse). New filenames,
        // the no-overwrite rule, and the isolated temporary folder prevent
        // modification of those existing inputs.
        string parent = Path.GetDirectoryName(workspace) ??
            throw new InvalidDataException("M195_NO_WORKSPACE_PARENT");
        RequireNoReparseAncestors(parent);
        var drive = new DriveInfo(Path.GetPathRoot(workspace)!);
        if (drive.AvailableFreeSpace < MinimumFreeBytes)
            throw new IOException("M195_SOURCE_EXTRACTION_NEEDS_8GB_FREE");

        // Source extraction selection is derived from the PINNED manifest,
        // never from an untrusted caller-provided list or archive contents.
        string archiveRoot = OfficialAmd26111SourceContract.VerifiedFrozenM194
            .RootInInstallerArchive.TrimEnd('/');
        var selected = inventory.Entries
            .Select(entry => archiveRoot + "/" + entry.Relative)
            .Append(archiveRoot + "/" +
                OfficialAmd26111SourceContract.VerifiedFrozenM194.OriginalMainCatalogRelative)
            .ToArray();
        if (selected.Length != 195 || selected.Distinct(
                StringComparer.OrdinalIgnoreCase).Count() != 195)
            throw new InvalidDataException("M195_ORIGINAL_SELECTION_CLOSURE_NOT_195");

        string temp = Path.Combine(parent,
            ".finesse-m195-extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            string output = Path.Combine(temp, "Extracted");
            Directory.CreateDirectory(output);
            string listFile = Path.Combine(temp, "SelectedOriginalAMD.txt");
            await File.WriteAllLinesAsync(listFile,
                selected.Select(path => path.Replace('/', '\\')),
                new UTF8Encoding(false), cancellationToken);
            string stdout = Path.Combine(temp, "7zr-stdout.txt");
            string stderr = Path.Combine(temp, "7zr-stderr.txt");
            var start = new ProcessStartInfo(extractor)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("x");
            start.ArgumentList.Add("-y");
            start.ArgumentList.Add("-o" + output);
            start.ArgumentList.Add("-i@" + listFile);
            start.ArgumentList.Add(verified.FullPath);
            using var process = Process.Start(start) ??
                throw new InvalidOperationException("M195_7ZR_PROCESS_NOT_STARTED");
            try
            {
                await using var stdoutFile = new FileStream(stdout, FileMode.CreateNew);
                await using var stderrFile = new FileStream(stderr, FileMode.CreateNew);
                await Task.WhenAll(
                    process.StandardOutput.BaseStream.CopyToAsync(stdoutFile, cancellationToken),
                    process.StandardError.BaseStream.CopyToAsync(stderrFile, cancellationToken),
                    process.WaitForExitAsync(cancellationToken));
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
            int code = process.ExitCode;
            // Frozen M194 accepted 7zr exit 1 only after independent, complete
            // file-level validation. Exit 1 alone is NEVER treated as success.
            if (code is not (0 or 1))
                throw new InvalidDataException("M195_7ZR_PROCESS_FAILED: " + code);

            string display = Path.Combine(output,
                archiveRoot.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(display))
                throw new InvalidDataException("M195_OFFICIAL_DISPLAY_ROOT_MISSING");
            RequireNoReparseAncestors(display);

            foreach (AmdSourceFileProof item in inventory.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(display,
                    item.Relative.Replace('/', Path.DirectorySeparatorChar));
                RequireNoReparseAncestors(path);
                if (!File.Exists(path) ||
                    await HashFileAsync(path, cancellationToken) != item.Sha256)
                    throw new InvalidDataException(
                        "M195_EXTRACTED_ORIGINAL_SHA_FAILED: " + item.Relative);
            }
            string catalogPath = Path.Combine(display,
                OfficialAmd26111SourceContract.VerifiedFrozenM194.OriginalMainCatalogRelative);
            RequireNoReparseAncestors(catalogPath);
            if (!File.Exists(catalogPath))
                throw new InvalidDataException("M195_EXTRACTED_ORIGINAL_CAT_MISSING");
            string catalogHash = await HashFileAsync(catalogPath, cancellationToken);
            string[] files = Directory.GetFiles(display, "*", SearchOption.AllDirectories);
            if (files.Length != 195)
                throw new InvalidDataException("M195_EXTRACTED_FILE_CLOSURE_NOT_195");
            foreach (string file in files)
                RequireNoReparseAncestors(file);

            string receipt = Path.Combine(temp, "M195-VerifiedOriginalExtraction.json");
            await File.WriteAllTextAsync(receipt, JsonSerializer.Serialize(new
            {
                Schema = "Finesse.M195.OriginalAmdSourceExtraction.v1",
                VerifiedInstallerSha256 = verified.Sha256,
                VerifiedInstallerBytes = verified.Bytes,
                VerifiedPublisher = verified.SignerSubject,
                VerifiedExtractorSha256 = Pinned7zrSha256,
                SourceInventorySha256 = inventory.CanonicalSha256,
                OriginalCatalogSha256 = catalogHash,
                OriginalFilesVerified = 194,
                OriginalCatalogExtracted = true,
                ExtractorExit = code,
                OfficialCatalogSignatureChecked = false,
                DriverCatalogGenerated = false,
                DriverInstalled = false,
                WindowsTrustModified = false,
                RebootScheduled = false
            }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), cancellationToken);
            Directory.Move(temp, workspace);
            return new VerifiedExtraction(
                Path.Combine(workspace, "Extracted",
                    archiveRoot.Replace('/', Path.DirectorySeparatorChar)),
                workspace, inventory.CanonicalSha256, catalogHash,
                inventory.Entries.Count, code,
                Path.Combine(workspace, "M195-VerifiedOriginalExtraction.json"));
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
    }

    private static void RequireNoReparseAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_SOURCE_PATH_REPARSE_POINT");
            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(current, parent, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }
}
