using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Isolated real M140 software-file preparation. Source AMD 26.11.1 already
/// includes exact original ccc2_install.exe in the 194-file hash manifest.
/// Uses pinned 7zr plus the pinned official 7-Zip 26.06 SFX as a portable
/// NSIS-capable extractor, NEVER installing 7-Zip or running AMD's SFX.
/// </summary>
internal static class M195AmdSettingsMsiPreparation
{
    internal sealed record Prepared(
        string MsiPath, string MsiSha256, string CccSourcePath,
        string ExtractionEvidence, string DriverVersionManifest,
        M195WindowsAmdSettingsMsiIdentity.PackageIdentity Identity);

    internal static async Task<Prepared> PrepareAsync(
        OfficialAmdInstallerVerifier.VerifiedSource originalAmd,
        Amd26111ArchiveExtraction.VerifiedExtraction extracted,
        string pinned7zr,
        string officialPortable7zipSfx,
        string newWorkspace,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalAmd);
        ArgumentNullException.ThrowIfNull(extracted);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AMD Settings extraction requires Windows.");
        var sourceContract = OfficialAmd26111SourceContract.VerifiedFrozenM194;
        if (originalAmd.Sha256 != sourceContract.InstallerSha256 ||
            originalAmd.Bytes != sourceContract.ExactInstallerBytes ||
            extracted.SourceManifestSha256 !=
                Amd26111SourceInventory.PinnedOriginalManifestSha256 ||
            extracted.OriginalFilesVerified != 194)
            throw new InvalidDataException("M195_M140_ORIGINAL_AMD_SOURCE_NOT_VERIFIED");
        var actualAmd = await OfficialAmdInstallerVerifier.Verify26111Async(
            originalAmd.FullPath, token);
        if (actualAmd.Sha256 != originalAmd.Sha256)
            throw new InvalidDataException("M195_M140_ORIGINAL_AMD_CHANGED");

        string ccc = Path.Combine(extracted.DisplayRoot,
            M195AmdSettings26111.SourceCccRelative.Replace('/',
                Path.DirectorySeparatorChar));
        RequireFile(ccc, M195AmdSettings26111.CccBytes,
            M195AmdSettings26111.CccSha256, "CCC2");
        var cccSignature = OfficialAmdInstallerVerifier.InspectWindowsSignature(ccc, token);
        if (!cccSignature.Trusted ||
            !cccSignature.Subject.Contains("Advanced Micro Devices",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_M140_CCC2_AMD_SIGNATURE_INVALID");

        string reduced = Path.GetFullPath(pinned7zr);
        if (!string.Equals(Path.GetFileName(reduced), "7zr.exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_M140_UNEXPECTED_7ZR_NAME");
        RequireFile(reduced, Amd26111ArchiveExtraction.Pinned7zrBytes,
            Amd26111ArchiveExtraction.Pinned7zrSha256, "7ZR");

        string sfx = Path.GetFullPath(officialPortable7zipSfx);
        if (!string.Equals(Path.GetFileName(sfx),
                M195AmdSettings26111.Portable7zipReleaseFileName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_M140_UNEXPECTED_7ZIP_RELEASE");
        RequireFile(sfx, null,
            M195AmdSettings26111.Portable7zipReleaseSha256, "7ZIP_SFX");

        string workspace = Path.GetFullPath(newWorkspace);
        if (Directory.Exists(workspace) || File.Exists(workspace))
            throw new IOException("M195_M140_NEW_SOFTWARE_WORKSPACE_REQUIRED");
        string parent = Path.GetDirectoryName(workspace) ??
            throw new InvalidDataException("M195_M140_WORKSPACE_PARENT_MISSING");
        RejectReparseAncestors(parent);
        Directory.CreateDirectory(workspace);
        string sevenDir = Path.Combine(workspace, "Portable7zip");
        string output = Path.Combine(workspace, "AMDSettings");
        Directory.CreateDirectory(sevenDir);
        Directory.CreateDirectory(output);
        try
        {
            int portableExit = await RunExtractorAsync(reduced,
                ["x", "-y", "-t7z", "-o" + sevenDir, sfx, "7z.exe", "7z.dll"],
                TimeSpan.FromMinutes(4), token);
            if (portableExit is not (0 or 1))
                throw new InvalidDataException("M195_M140_PORTABLE_7ZIP_EXTRACTION_EXIT_" + portableExit);
            string seven = Path.Combine(sevenDir, "7z.exe");
            string sevenDll = Path.Combine(sevenDir, "7z.dll");
            RejectReparseAncestors(seven);
            RejectReparseAncestors(sevenDll);
            if (!File.Exists(seven) || !File.Exists(sevenDll) ||
                new FileInfo(seven).Length < 50_000 ||
                new FileInfo(sevenDll).Length < 100_000)
                throw new InvalidDataException("M195_M140_PORTABLE_7ZIP_FILES_INCOMPLETE");
            int msiExit = await RunExtractorAsync(seven,
                ["x", "-tnsis", "-y", "-o" + output, ccc,
                    @"CN\cnext\cnext64\ccc-next64.msi",
                    @"CN\cnext\cnext64\ccc-next64.json",
                    "cccmanifest_64.json"],
                TimeSpan.FromMinutes(8), token);
            if (msiExit is not (0 or 1))
                throw new InvalidDataException("M195_M140_NSIS_EXTRACT_EXIT_" + msiExit);

            string msi = Path.Combine(output,
                M195AmdSettings26111.MsiRelative.Replace('/',
                    Path.DirectorySeparatorChar));
            RequireFile(msi, M195AmdSettings26111.MsiBytes,
                M195AmdSettings26111.MsiSha256, "CCC_NEXT64_MSI");
            var signature = OfficialAmdInstallerVerifier.InspectWindowsSignature(msi, token);
            if (!signature.Trusted ||
                (!string.IsNullOrWhiteSpace(signature.Subject) &&
                 !signature.Subject.Contains("Advanced Micro Devices",
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("M195_M140_MSI_WINVERIFYTRUST_FAILED");

            string component = Path.Combine(output,
                "CN", "cnext", "cnext64", "ccc-next64.json");
            string manifest = Path.Combine(output, "cccmanifest_64.json");
            RejectReparseAncestors(component);
            RejectReparseAncestors(manifest);
            if (!File.Exists(component) || !File.Exists(manifest) ||
                !File.ReadAllText(component).Contains(
                    M195AmdSettings26111.TargetDriverVersion, StringComparison.Ordinal) ||
                !File.ReadAllText(component).Contains(
                    "ccc-next64.msi", StringComparison.Ordinal))
                throw new InvalidDataException("M195_M140_CCC_MANIFEST_DRIVER_VERSION_MISMATCH");

            var identity = M195WindowsAmdSettingsMsiIdentity.ReadExactMsiProperties(msi);
            M195WindowsAmdSettingsMsiIdentity.RequirePinned26111(identity);
            string evidence = Path.Combine(workspace, "M195-MSI-SourceVerification.json");
            await File.WriteAllTextAsync(evidence,
                JsonSerializer.Serialize(new {
                    Schema = "Finesse.M195.M140.MSIIdentity.v1",
                    SourceAmdSha256 = actualAmd.Sha256,
                    SourceCccSha256 = M195AmdSettings26111.CccSha256,
                    OriginalSourceManifestSha256 = extracted.SourceManifestSha256,
                    Portable7zipSha256 = M195AmdSettings26111.Portable7zipReleaseSha256,
                    ActualPortableExeSha256 = Hash(seven),
                    ActualPortableDllSha256 = Hash(sevenDll),
                    MsiSha256 = Hash(msi),
                    ActualMsiSignatureStatus = signature.WindowsStatus,
                    ActualMsiSigner = signature.Subject,
                    Identity = identity,
                    PortableExtractorExit = portableExit,
                    NestedExtractorExit = msiExit,
                    NoMsiExecutedYet = true,
                    GpuChanged = false,
                    RebootRequested = false
                }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), token);
            return new Prepared(msi, M195AmdSettings26111.MsiSha256,
                ccc, evidence, component, identity);
        }
        catch
        {
            // Preserve the isolated extraction/evidence workspace for review.
            // No MSI, driver installation or system mutation occurred here.
            throw;
        }
    }

    private static async Task<int> RunExtractorAsync(string exe,
        IReadOnlyList<string> args, TimeSpan timeout, CancellationToken token)
    {
        var start = new ProcessStartInfo(exe) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (string a in args) start.ArgumentList.Add(a);
        using var process = Process.Start(start) ??
            throw new IOException("M195_M140_EXTRACTOR_DID_NOT_START");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(limit.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(limit.Token));
            if (stdout.Result.Length > 2_000_000 || stderr.Result.Length > 2_000_000)
                throw new InvalidDataException("M195_M140_EXTRACTOR_OUTPUT_TOO_LARGE");
            return process.ExitCode;
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
    }

    private static void RequireFile(string path, long? bytes, string expected,
        string label)
    {
        RejectReparseAncestors(path);
        if (!File.Exists(path) ||
            (bytes is not null && new FileInfo(path).Length != bytes.Value) ||
            !Hash(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_M140_AUTHENTICATED_FILE_FAILED_" + label);
    }

    private static string Hash(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RejectReparseAncestors(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_M140_REPARSE_PATH_FORBIDDEN");
            string? parent = Path.GetDirectoryName(cursor);
            if (parent == cursor) break;
            cursor = parent;
        }
    }
}
