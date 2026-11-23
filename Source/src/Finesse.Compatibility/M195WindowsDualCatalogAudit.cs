using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Read-only audit of the actual published AMD 26.11.1 Driver Store package.
/// Mirrors frozen M194 M191's original/local SignTool membership tests and
/// registered catalog enumeration for all 14 unchanged graphics members.
/// No certificate changes, catalog registration, GPU bind or reboot.
/// </summary>
public static class M195WindowsDualCatalogAudit
{
    public sealed record Audit(
        M195WindowsDriverStoreLocator.LocatedPackage DriverStore,
        M195DualCatalogCoverage.Snapshot Snapshot,
        M195DualCatalogCoverage.Assessment Assessment,
        string EvidenceFile);

    public static async Task<Audit> CaptureAsync(
        string publishedInf, string preparedCandidateRoot,
        string originalAmdCatalog, string expectedLocalCatalogSha256,
        string verifiedSignToolExe, string freshEvidenceFile,
        CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Catalog audit needs Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(freshEvidenceFile);

        string root = Path.GetFullPath(preparedCandidateRoot);
        string originalCat = Path.GetFullPath(originalAmdCatalog);
        string localCat = Path.Combine(root, "u0206030.cat");
        string signTool = Path.GetFullPath(verifiedSignToolExe);
        string evidence = Path.GetFullPath(freshEvidenceFile);
        string originalSha = M195LiveSigningPreflight.PinnedOriginalAmdMainCatalogSha256;
        string localSha = RequireSha(expectedLocalCatalogSha256);
        if (originalSha == localSha)
            throw new InvalidDataException("M195_AUDIT_CATALOG_IDENTITIES_COLLIDE");
        foreach (string path in new[] { root, originalCat, localCat, signTool,
                Path.GetDirectoryName(evidence)! })
            RejectReparseAncestors(path);
        if (File.Exists(evidence))
            throw new IOException("M195_AUDIT_EXISTING_EVIDENCE_REFUSED");
        if (!Directory.Exists(root) || !File.Exists(originalCat) ||
            !File.Exists(localCat) || !File.Exists(signTool) ||
            Sha(originalCat) != originalSha || Sha(localCat) != localSha ||
            Sha(signTool) != M195LiveSigningPreflight.PinnedMicrosoftSignToolSha256)
            throw new InvalidDataException("M195_AUDIT_CATALOG_OR_SIGNTOOL_SHA_FAILED");

        var located = M195WindowsDriverStoreLocator.RequireFinesse26111Package(
            publishedInf, Path.Combine(root, "u0206030.inf"), localCat, localSha);
        var receipts = new List<object>();
        bool whcp = await VerifyWhcpIdentityAsync(originalCat, receipts, token);
        bool originalKp = await SignToolAsync(signTool,
            ["verify", "/kp", "/v", originalCat], "official-kernel-policy", receipts, token);
        bool localPa = await SignToolAsync(signTool,
            ["verify", "/pa", "/v", localCat], "finesse-pnp-policy", receipts, token);

        var members = new List<M195DualCatalogCoverage.FileEvidence>(14);
        var expectedTargets = M195AmdDualTrustTargets.For26111;
        if (expectedTargets.Count != 14)
            throw new InvalidDataException("M195_AUDIT_TARGET_COUNT_NOT_14");
        foreach (var (target, index) in expectedTargets.Select((t, i) => (t, i)))
        {
            token.ThrowIfCancellationRequested();
            string member = Path.GetFullPath(Path.Combine(located.DriverStoreRoot,
                target.Relative.Replace('/', Path.DirectorySeparatorChar)));
            string prefix = located.DriverStoreRoot.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!member.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("M195_AUDIT_MEMBER_ESCAPED_DRIVERSTORE");
            RejectReparseAncestors(member);
            if (!File.Exists(member) ||
                !string.Equals(Sha(member), target.OriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "M195_AUDIT_GRAPHICS_HASH_MISMATCH:" + target.Relative);

            bool localMember = await SignToolAsync(signTool,
                ["verify", "/pa", "/v", "/c", localCat, member],
                "local-" + index.ToString("D2"), receipts, token);
            bool originalMember = await SignToolAsync(signTool,
                ["verify", "/pa", "/v", "/c", originalCat, member],
                "original-" + index.ToString("D2"), receipts, token);
            var membership = M195WindowsCatalogRegistry.ObserveDualMembership(
                member, target.OriginalSha256, originalSha, localSha);
            members.Add(new M195DualCatalogCoverage.FileEvidence(
                target.Relative, target.OriginalSha256, originalMember, localMember,
                membership.AuthenticAmdCatalogRegistered,
                membership.LocalFinesseCatalogRegistered));
        }

        var snapshot = new M195DualCatalogCoverage.Snapshot(
            true, true, whcp, originalKp, localPa, members.AsReadOnly());
        var assessment = M195DualCatalogCoverage.Evaluate(snapshot);
        string? parent = Path.GetDirectoryName(evidence);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("M195 audit evidence parent missing.");
        string json = JsonSerializer.Serialize(new {
            Schema = "Finesse.M195.ReadOnly14x4DualTrust.v1",
            CreatedUtc = DateTimeOffset.UtcNow,
            PublishedInf = located.PublishedInf,
            DriverStoreInf = located.DriverStoreInfPath,
            DriverStoreRoot = located.DriverStoreRoot,
            OriginalAmdCatSha256 = originalSha,
            FinesseCatSha256 = localSha,
            Snapshot = snapshot, Assessment = assessment,
            SignatureReceipts = receipts,
            CatalogRegistrationAttempted = false,
            DriverBindAttempted = false,
            CertificateModified = false,
            RebootRequested = false
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(evidence, json, new UTF8Encoding(false), token);
        return new Audit(located, snapshot, assessment, evidence);
    }

    private static Task<bool> VerifyWhcpIdentityAsync(
        string originalCat, List<object> receipts, CancellationToken token)
    {
        // Frozen M191 uses Get-AuthenticodeSignature + exact WHCP subject.
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        string command =
            "$ErrorActionPreference='Stop';" +
            "$signature=Get-AuthenticodeSignature -LiteralPath $env:FINESSE_M195_AMD_ORIGINAL_CAT;" +
            "if($null -eq $signature -or $null -eq $signature.SignerCertificate){exit 4};" +
            "if([string]$signature.Status -cne 'Valid'){exit 5};" +
            "if($signature.SignerCertificate.Subject -notlike '*Microsoft Windows Hardware Compatibility Publisher*'){exit 6};" +
            "exit 0";
        var start = new ProcessStartInfo(powershell) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["FINESSE_M195_AMD_ORIGINAL_CAT"] = originalCat;
        foreach (string arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive",
            "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
            start.ArgumentList.Add(arg);
        return RunAsync(start, "authentic-original-whcp", receipts, token);
    }

    private static Task<bool> SignToolAsync(string executable, string[] args,
        string tag, List<object> receipts, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        return RunAsync(start, tag, receipts, token);
    }

    private static async Task<bool> RunAsync(ProcessStartInfo start, string tag,
        List<object> receipts, CancellationToken token)
    {
        using Process child = Process.Start(start) ??
            throw new IOException("M195_AUDIT_PROCESS_FAILED:" + tag);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
        linked.CancelAfter(TimeSpan.FromMinutes(2));
        string stdout, stderr;
        try
        {
            Task<string> outTask = child.StandardOutput.ReadToEndAsync(linked.Token);
            Task<string> errTask = child.StandardError.ReadToEndAsync(linked.Token);
            await Task.WhenAll(outTask, errTask, child.WaitForExitAsync(linked.Token));
            stdout = outTask.Result;
            stderr = errTask.Result;
        }
        catch
        {
            if (!child.HasExited)
            {
                try { child.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await child.WaitForExitAsync(CancellationToken.None);
            }
            throw;
        }
        receipts.Add(new {
            Tag = tag, ExitCode = child.ExitCode,
            Stdout = stdout[..Math.Min(stdout.Length, 2500)],
            Stderr = stderr[..Math.Min(stderr.Length, 2500)]
        });
        return child.ExitCode == 0;
    }

    private static string Sha(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static string RequireSha(string sha)
    {
        if (sha.Length != 64 || sha.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("M195_AUDIT_SHA256_FORMAT_INVALID");
        return sha.ToUpperInvariant();
    }

    private static void RejectReparseAncestors(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_AUDIT_REPARSE_PATH");
            string? parent = Path.GetDirectoryName(cursor);
            if (parent == cursor) break;
            cursor = parent;
        }
    }
}
