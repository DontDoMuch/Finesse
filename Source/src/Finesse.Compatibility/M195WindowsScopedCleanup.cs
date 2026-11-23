using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Finesse.Compatibility;

/// <summary>
/// Frozen M192 inactive-package cleanup, ported to native C# on Windows.
/// It is only optional pre-install maintenance AFTER a healthy, SHA-verified
/// original-driver export. NO /force, /uninstall, Root or LocalMachine trust
/// deletion, OEM extension deletion, or current GPU driver removal.
/// </summary>
internal static class M195WindowsScopedCleanup
{
    internal sealed record Candidate(string PublishedInf, string Sha256);
    internal sealed record PackageAction(string PublishedInf, string Sha256,
        int PnpUtilExitCode, bool Removed, string Output);
    internal sealed record CertificateAction(string Thumbprint, string Action);
    internal sealed record Result(string Status, string EvidenceFile,
        int InactiveMatches, int Removed, IReadOnlyList<string> Errors);

    private static readonly Regex OemInf = new(
        @"\Aoem[0-9]+\.inf\z", RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex DisplayClass = new(
        @"(?im)^\s*Class\s*=\s*Display\b",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex OrphanCertSubject = new(
        @"\ACN=Finesse-(?:M175-Offline|M164-OFFLINE)-[0-9A-F]{32}\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    internal static bool IsScopedInactiveFinesseInf(
        string infText, string publishedInf, string baselineInf,
        HandheldProfile profile)
    {
        ArgumentNullException.ThrowIfNull(infText);
        ArgumentNullException.ThrowIfNull(profile);
        if (!OemInf.IsMatch(publishedInf) ||
            !OemInf.IsMatch(baselineInf) ||
            publishedInf.Equals(baselineInf, StringComparison.OrdinalIgnoreCase) ||
            !DisplayClass.IsMatch(infText))
            return false;
        string (deviceId, subsystem, policy) = profile == HandheldProfiles.Go1
            ? ("15BF", "381217AA", "Go1") :
            profile == HandheldProfiles.GoS
            ? ("15BF", "380C17AA", "GoS") :
            profile == HandheldProfiles.Go2
            ? ("150E", "381C17AA", "Go2") :
            throw new InvalidDataException("M195_M192_UNSUPPORTED_HARDWARE_PROFILE");
        return infText.Contains("DEV_" + deviceId,
                StringComparison.OrdinalIgnoreCase) &&
            infText.Contains("SUBSYS_" + subsystem,
                StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(infText,
                @"(?im)^\s*\[\s*LegionGo_Finesse_" + policy +
                @"_Policy\s*\]", RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(2));
    }

    internal static async Task<Result> RunAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline,
        string evidenceDirectory, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Scoped package cleanup requires Windows.");
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        string root = Path.GetFullPath(evidenceDirectory);
        RejectReparseAncestors(root);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("M195_M192_FRESH_EVIDENCE_DIRECTORY_REQUIRED");
        Directory.CreateDirectory(root);
        string receipt = Path.Combine(root, "M195-ScopedCleanup.json");
        string status = "STARTED";
        var candidates = new List<Candidate>();
        var packages = new List<PackageAction>();
        var certificates = new List<CertificateAction>();
        var errors = new List<string>();
        string? baselineHash = null;

        try
        {
            if (!original.BackupEligible || !baseline.Verified ||
                string.IsNullOrWhiteSpace(baseline.VerifiedExportDirectory) ||
                original.ProblemCode != 0)
            {
                status = "SKIPPED_NO_VERIFIED_HEALTHY_BASELINE";
            }
            else
            {
                if (device.Profile != HandheldProfiles.Resolve(device.ExactHardwareId) ||
                    !string.Equals(baseline.RecoveryInf, original.PublishedInf,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("M195_M192_PROFILE_OR_BACKUP_INVALID");

                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    if (!new WindowsPrincipal(identity).IsInRole(
                            WindowsBuiltInRole.Administrator))
                        throw new UnauthorizedAccessException(
                            "M195_M192_REQUIRES_EXISTING_ELEVATED_INSTALLER");
                }
                string baselineInf = M195WindowsBindIdentity.RequirePublishedOemInf(
                    original.PublishedInf);
                string infDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
                string baselinePath = Path.Combine(infDir, baselineInf);
                RejectReparseAncestors(infDir);
                RejectReparseAncestors(baselinePath);
                if (!File.Exists(baselinePath))
                    throw new FileNotFoundException(
                        "Original assigned GPU INF missing.", baselinePath);
                baselineHash = Hash(baselinePath);
                await RequireBaselineUnchangedAsync(
                    device, original, baselinePath, baselineHash, token);

                string pnputil = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "System32", "pnputil.exe");
                RejectReparseAncestors(pnputil);
                if (!File.Exists(pnputil))
                    throw new FileNotFoundException(
                        "Windows PnPUtil missing.", pnputil);

                foreach (string file in Directory.EnumerateFiles(
                    infDir, "oem*.inf", SearchOption.TopDirectoryOnly))
                {
                    token.ThrowIfCancellationRequested();
                    string published = Path.GetFileName(file);
                    if (!OemInf.IsMatch(published) ||
                        published.Equals(baselineInf,
                            StringComparison.OrdinalIgnoreCase) ||
                        new FileInfo(file).Length > 6_000_000)
                        continue;
                    RejectReparseAncestors(file);
                    string body = await File.ReadAllTextAsync(file, token);
                    if (!IsScopedInactiveFinesseInf(
                            body, published, baselineInf, device.Profile))
                        continue;

                    // The original M192 contract only examines INACTIVE driver
                    // packages. Check all present Display devnodes, and rely
                    // on PnPUtil's default in-use refusal as a second barrier.
                    var allDisplay = await M195WindowsGpuInventory.EnumerateAsync(token);
                    if (allDisplay.Any(d =>
                        string.Equals(d.PublishedInf, published,
                            StringComparison.OrdinalIgnoreCase)))
                        continue;
                    string hash = Hash(file);
                    candidates.Add(new Candidate(published, hash));
                    await RequireBaselineUnchangedAsync(
                        device, original, baselinePath, baselineHash, token);
                    if (!File.Exists(file) ||
                        !string.Equals(Hash(file), hash, StringComparison.Ordinal))
                        throw new InvalidDataException("M195_M192_CANDIDATE_CHANGED");

                    var processResult = await DeleteUnusedPackageAsync(
                        pnputil, published, token);
                    bool removed = processResult.Exit == 0 && !File.Exists(file);
                    packages.Add(new PackageAction(published, hash,
                        processResult.Exit, removed, processResult.Text));
                    // Always independently verify the active baseline after
                    // every potential mutation; never rely on PnPUtil exit 0.
                    await RequireBaselineUnchangedAsync(
                        device, original, baselinePath, baselineHash, token);
                }
                certificates.AddRange(RemoveOnlyOrphanUserMyCertificates());
                await RequireBaselineUnchangedAsync(
                    device, original, baselinePath, baselineHash, token);
                status = "PASS_SCOPED_CLEANUP_ACTIVE_TRUST_PRESERVED";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            status = "CANCELLED_REQUIRES_REVIEW";
            errors.Add("User cancellation during optional scoped cleanup");
            throw;
        }
        catch (Exception ex)
        {
            status = "INCOMPLETE_REQUIRES_REVIEW";
            errors.Add(ex.GetType().Name + ": " + ex.Message);
            // An optional cleanup problem is evidence, not a new AMD driver
            // installation acceptance gate or a bulk cleanup authorization.
        }
        finally
        {
            await File.WriteAllTextAsync(receipt,
                JsonSerializer.Serialize(new {
                    Schema = "Finesse.M195.M192.ScopedCleanup.v1",
                    Status = status, DeviceInstance = device.InstanceId,
                    ProfileHardwareId = device.ExactHardwareId,
                    OriginalInf = original.PublishedInf,
                    OriginalVersion = original.Version,
                    OriginalInfSha256 = baselineHash,
                    Candidates = candidates, PackageActions = packages,
                    CurrentUserMyCertificateActions = certificates,
                    CurrentUserRootModified = false,
                    LocalMachineCertificatesModified = false,
                    OriginalAssignedGpuDriverIntentionallyRemoved = false,
                    OEMExtensionsModified = false,
                    WindowsCatalogsModified = false,
                    RecoveryCheckpointsModified = false,
                    PnpUtilFlags = "/delete-driver <exact inactive oemNN.inf>",
                    ForceOrUninstallFlagsUsed = false,
                    Errors = errors, CompletedUtc = DateTimeOffset.UtcNow
                }, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false), CancellationToken.None);
        }
        return new Result(status, receipt,
            candidates.Count, packages.Count(p => p.Removed),
            errors.AsReadOnly());
    }

    private static async Task<(int Exit, string Text)> DeleteUnusedPackageAsync(
        string pnputil, string inf, CancellationToken token)
    {
        var start = new ProcessStartInfo(pnputil) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("/delete-driver");
        start.ArgumentList.Add(inf);
        using Process process = Process.Start(start) ??
            throw new IOException("M195_M192_PNPUTIL_NOT_STARTED");
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromMinutes(3));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(bounded.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(bounded.Token);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(bounded.Token));
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
        string combined = stdout.Result + "\n" + stderr.Result;
        return (process.ExitCode,
            combined[..Math.Min(combined.Length, 3000)]);
    }

    private static List<CertificateAction> RemoveOnlyOrphanUserMyCertificates()
    {
        var changed = new List<CertificateAction>();
        using var my = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        my.Open(OpenFlags.ReadWrite);
        foreach (X509Certificate2 item in my.Certificates)
        {
            if (!OrphanCertSubject.IsMatch(item.Subject))
                continue;
            string thumb = item.Thumbprint.ToUpperInvariant();
            if (!Regex.IsMatch(thumb, @"\A[0-9A-F]{40}\z",
                    RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
                continue;
            using var root = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            root.Open(OpenFlags.ReadOnly);
            bool presentInRoot = root.Certificates.Find(
                X509FindType.FindByThumbprint, thumb, validOnly: false).Count > 0;
            if (presentInRoot)
            {
                changed.Add(new CertificateAction(thumb,
                    "PRESERVED_ROOT_ENTRY_PRESENT"));
                continue;
            }
            my.Remove(item);
            changed.Add(new CertificateAction(thumb,
                "REMOVED_DETACHED_ORPHAN_CURRENTUSER_MY"));
        }
        return changed;
    }

    private static async Task RequireBaselineUnchangedAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.AssignedDriver original,
        string originalInfPath, string expectedHash, CancellationToken token)
    {
        var observed = M195WindowsGpuInventory.RequireUniqueExactGpu(
            await M195WindowsGpuInventory.EnumerateAsync(token));
        if (observed.Profile != device.Profile ||
            !string.Equals(observed.Display.InstanceId, device.InstanceId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observed.Display.PublishedInf, original.PublishedInf,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(observed.Display.Version, original.Version,
                StringComparison.Ordinal) ||
            observed.Display.ProblemCode != 0 ||
            !File.Exists(originalInfPath) ||
            !string.Equals(Hash(originalInfPath), expectedHash,
                StringComparison.Ordinal))
            throw new InvalidDataException("M195_M192_ACTIVE_BASELINE_CHANGED");
    }

    private static string Hash(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void RejectReparseAncestors(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((Directory.Exists(cursor) || File.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_M192_REPARSE_PATH_REFUSED");
            string? parent = Path.GetDirectoryName(cursor);
            if (string.Equals(parent, cursor, StringComparison.OrdinalIgnoreCase))
                break;
            cursor = parent;
        }
    }
}
