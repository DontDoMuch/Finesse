using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Finesse.Compatibility;

/// <summary>
/// Native Windows PnPUtil driver EXPORT for the exactly assigned, healthy OEM
/// display INF. Ported from frozen M194 M182.BackupEligibleDriverAsync and
/// PnpUtilDriverPackageExporter. Export only; no driver deletion, installation,
/// device binding, certificate-store changes, or restart.
/// </summary>
public static class M195WindowsRecoveryBaseline
{
    public sealed record VerifiedExport(
        string OriginalInf, string OriginalDriverVersion, string PackageDirectory,
        string ManifestPath, string EvidencePath, int VerifiedFiles);

    private static readonly Regex ExactOemInf = new(
        @"^oem[0-9]+\.inf$", RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    public static async Task<VerifiedExport> ExportAsync(
        M195WindowsGpuInventory.ExactGpu target, string freshRecoveryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("pnputil export requires Windows");
        if (M195WindowsGpuInventory.RequireUniqueExactGpu([target.Display]).Profile != target.Profile ||
            !M195WindowsGpuInventory.EligibleForRecoveryExport(target.Display))
            throw new InvalidDataException("M195_RECOVERY_ACTIVE_GPU_INELIGIBLE");
        string inf = target.Display.PublishedInf;
        if (!ExactOemInf.IsMatch(inf) || Path.GetFileName(inf) != inf)
            throw new InvalidDataException("M195_RECOVERY_EXACT_OEM_INF_REQUIRED");
        ArgumentException.ThrowIfNullOrWhiteSpace(freshRecoveryRoot);
        string outputRoot = Path.GetFullPath(freshRecoveryRoot);
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new IOException("M195_RECOVERY_ROOT_MUST_BE_NEW");
        string parent = Path.GetDirectoryName(outputRoot) ??
            throw new InvalidDataException("M195_RECOVERY_PARENT_MISSING");
        RequireNoReparseAncestors(parent);

        string systemPnpUtil = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "pnputil.exe");
        RequireNoReparseAncestors(systemPnpUtil);
        if (!File.Exists(systemPnpUtil))
            throw new FileNotFoundException("Native Windows pnputil.exe missing.", systemPnpUtil);

        // Export package into its own NEW directory; do not mutate existing
        // recovery checkpoints if Windows/PnPUtil fails halfway through.
        string temp = Path.Combine(parent,
            ".finesse-m195-backup-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            string exported = Path.Combine(temp, "ExportedDriver");
            Directory.CreateDirectory(exported);
            var start = new ProcessStartInfo(systemPnpUtil)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add("/export-driver");
            start.ArgumentList.Add(inf);
            start.ArgumentList.Add(exported);
            using Process process = Process.Start(start) ??
                throw new IOException("M195_RECOVERY_PNPUTIL_NOT_STARTED");
            string stdout, stderr;
            using var limited = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limited.CancelAfter(TimeSpan.FromMinutes(3));
            try
            {
                Task<string> outTask = process.StandardOutput.ReadToEndAsync(limited.Token);
                Task<string> errTask = process.StandardError.ReadToEndAsync(limited.Token);
                await Task.WhenAll(outTask, errTask, process.WaitForExitAsync(limited.Token));
                stdout = outTask.Result;
                stderr = errTask.Result;
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
            await File.WriteAllTextAsync(Path.Combine(temp, "PnPUtil-stdout.txt"), stdout,
                new UTF8Encoding(false), cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(temp, "PnPUtil-stderr.txt"), stderr,
                new UTF8Encoding(false), cancellationToken);
            if (process.ExitCode != 0)
                throw new InvalidDataException("M195_RECOVERY_PNPUTIL_EXIT_" + process.ExitCode);

            string[] files = Directory.GetFiles(exported, "*",
                SearchOption.AllDirectories);
            if (files.Length is < 1 or > 2000 ||
                !files.Any(path => path.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("M195_RECOVERY_EXPORT_MISSING_DRIVER_INF");

            // Hash every exported file, detect traversal/reparse/duplicate
            // paths, and verify each byte by rehashing before final publish.
            var manifest = new SortedDictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireNoReparseAncestors(file);
                string relative = Path.GetRelativePath(exported, file)
                    .Replace('\\', '/');
                Amd26111SourceInventory.ValidateRelative(relative);
                if (!manifest.TryAdd(relative, FileHash(file)))
                    throw new InvalidDataException("M195_RECOVERY_DUPLICATE_EXPORT_FILE");
            }
            string manifestPath = Path.Combine(temp, "SHA256SUMS.txt");
            await File.WriteAllTextAsync(manifestPath, string.Concat(manifest
                .Select(item => item.Value + "  " + item.Key + "\n")),
                new UTF8Encoding(false), cancellationToken);
            foreach (var item in manifest)
            {
                string file = Path.Combine(exported,
                    item.Key.Replace('/', Path.DirectorySeparatorChar));
                if (FileHash(file) != item.Value)
                    throw new InvalidDataException("M195_RECOVERY_POSTEXPORT_SHA_MISMATCH");
            }
            string evidence = Path.Combine(temp, "M195-RecoveryBaseline.json");
            await File.WriteAllTextAsync(evidence, JsonSerializer.Serialize(new
            {
                Schema = "Finesse.M195.WindowsRecoveryExport.v1",
                ExactHardwareId = target.Profile.HardwareId,
                DeviceInstance = target.Display.InstanceId,
                PublishedInf = inf,
                OriginalDriverVersion = target.Display.Version,
                ProblemCode = target.Display.ProblemCode,
                ExportedFiles = manifest.Count,
                WindowsPnpUtilExit = process.ExitCode,
                ReadOnlyExport = true,
                RecoveryBindingAttempted = false
            }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), cancellationToken);

            Directory.Move(temp, outputRoot);
            return new VerifiedExport(inf, target.Display.Version,
                Path.Combine(outputRoot, "ExportedDriver"),
                Path.Combine(outputRoot, "SHA256SUMS.txt"),
                Path.Combine(outputRoot, "M195-RecoveryBaseline.json"),
                manifest.Count);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static string FileHash(string file)
    {
        using var input = new FileStream(file, FileMode.Open,
            FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static void RequireNoReparseAncestors(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_RECOVERY_REPARSE_POINT_FORBIDDEN");
            string? parent = Path.GetDirectoryName(cursor);
            if (parent == cursor)
                break;
            cursor = parent;
        }
    }
}
