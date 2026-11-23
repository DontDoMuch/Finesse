using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Concrete M179 retained LocalMachine signer. Creates a single local
/// nonexportable signing identity, adds its public certificate to Windows
/// Root and TrustedPublisher, signs the source-verified candidate CAT once,
/// checks /pa and INF catalog membership, and emits evidence.
/// The signed catalog is NOT a driver installation or a physical Code 0 proof.
/// Keep this disconnected from the GUI until the stage/bind/rollback adapter
/// can retain OR release the signer appropriately.
/// </summary>
internal static class M195LiveCatalogSigner
{
    internal sealed record Signed(
        string CatalogPath, string SignedCatalogSha256, string SignerThumbprint,
        string SignerSubject, string EvidencePath, bool TrustRetained);

    internal static async Task<Signed> SignAsync(
        M195LiveSigningPreflight.Verified verified,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(verified);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("M195 signer requires Windows.");
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("Single elevated installer process required.");
        }
        static string Hash(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        if (Hash(verified.UnsignedMainCatalog) != verified.UnsignedCatalogSha256 ||
            Hash(verified.PinnedSignTool) != M195LiveSigningPreflight.PinnedMicrosoftSignToolSha256 ||
            Hash(verified.OriginalAmdCatalog) != M195LiveSigningPreflight.PinnedOriginalAmdMainCatalogSha256)
            throw new InvalidDataException("M195_SIGN_FILE_CHANGED_AFTER_PREFLIGHT");
        VerifyAllMembers(verified);
        string dir = verified.EvidenceDirectory;
        string scriptPath = Path.Combine(dir, "M195-Live-Signer-Script.ps1");
        string proofPath = Path.Combine(dir, "M195-Live-Signer-Proof.json");
        string consolePath = Path.Combine(dir, "M195-Live-Signer-Console.txt");
        if (File.Exists(scriptPath) || File.Exists(proofPath) || File.Exists(consolePath))
            throw new InvalidDataException("M195_SIGN_ALREADY_ATTEMPTED");

        static string B64(string value) =>
            Convert.ToBase64String(Encoding.Unicode.GetBytes(value));
        string ps = Script
            .Replace("@CAT@", B64(verified.UnsignedMainCatalog), StringComparison.Ordinal)
            .Replace("@INF@", B64(verified.PreparedInf), StringComparison.Ordinal)
            .Replace("@TOOL@", B64(verified.PinnedSignTool), StringComparison.Ordinal)
            .Replace("@WORK@", B64(dir), StringComparison.Ordinal);
        await File.WriteAllTextAsync(scriptPath, ps, new UTF8Encoding(false), token);
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powershell))
            throw new FileNotFoundException("Windows PowerShell 5.1 is required.", powershell);
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] {
            "-NoLogo", "-NoProfile", "-NonInteractive",
            "-ExecutionPolicy", "Bypass", "-File", scriptPath })
            start.ArgumentList.Add(arg);

        using Process proc = Process.Start(start) ??
            throw new InvalidOperationException("M195_SIGN_POWERSHELL_DID_NOT_START");
        string stdout, stderr;
        using var limited = CancellationTokenSource.CreateLinkedTokenSource(token);
        limited.CancelAfter(TimeSpan.FromMinutes(4));
        try
        {
            Task<string> outTask = proc.StandardOutput.ReadToEndAsync(limited.Token);
            Task<string> errTask = proc.StandardError.ReadToEndAsync(limited.Token);
            await Task.WhenAll(outTask, errTask, proc.WaitForExitAsync(limited.Token));
            stdout = outTask.Result;
            stderr = errTask.Result;
        }
        catch
        {
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                await proc.WaitForExitAsync(CancellationToken.None);
            }
            throw;
        }
        await File.WriteAllTextAsync(consolePath,
            "ExitCode=" + proc.ExitCode + "\nSTDOUT\n" +
            stdout[..Math.Min(stdout.Length, 120_000)] + "\nSTDERR\n" +
            stderr[..Math.Min(stderr.Length, 120_000)],
            new UTF8Encoding(false), CancellationToken.None);
        if (proc.ExitCode != 0 || !File.Exists(proofPath))
            throw new InvalidDataException(
                "M195_SIGN_FAILED: consult M195-Live-Signer-Proof and Console evidence");
        using var doc = JsonDocument.Parse(
            await File.ReadAllTextAsync(proofPath, CancellationToken.None));
        var p = doc.RootElement;
        if (p.GetProperty("Status").GetString() != "PASS_INSTALLATION_SIGNER_RETAINED" ||
            p.GetProperty("SignExitCode").GetInt32() != 0 ||
            p.GetProperty("PaExitCode").GetInt32() != 0 ||
            p.GetProperty("InfExitCode").GetInt32() != 0 ||
            !p.GetProperty("TrustRetained").GetBoolean())
            throw new InvalidDataException("M195_SIGN_EVIDENCE_NOT_SUCCESS");
        string thumb = p.GetProperty("Thumbprint").GetString() ?? "";
        string subject = p.GetProperty("Subject").GetString() ?? "";
        if (!M195LiveSigningPreflight.IsOwnedSigner(thumb, subject))
            throw new InvalidDataException("M195_SIGN_CERTIFICATE_IDENTITY_INVALID");
        string finalSha = Hash(verified.UnsignedMainCatalog);
        VerifyAllMembers(verified);
        if (Hash(verified.OriginalAmdCatalog) !=
                M195LiveSigningPreflight.PinnedOriginalAmdMainCatalogSha256)
            throw new InvalidDataException("M195_SIGN_ORIGINAL_AMD_CATALOG_CHANGED");
        if (p.GetProperty("CatalogSha256").GetString() != finalSha ||
            finalSha == verified.UnsignedCatalogSha256)
            throw new InvalidDataException("M195_SIGN_CATALOG_NOT_CHANGED_AS_EXPECTED");
        foreach (StoreName store in new[] {
            StoreName.My, StoreName.Root, StoreName.TrustedPublisher })
        {
            using var installed = new X509Store(store, StoreLocation.LocalMachine);
            installed.Open(OpenFlags.ReadOnly);
            X509Certificate2Collection matches =
                installed.Certificates.Find(X509FindType.FindByThumbprint, thumb, false);
            if (matches.Count != 1 ||
                matches[0].Subject != subject ||
                (store == StoreName.My && !matches[0].HasPrivateKey))
                throw new InvalidDataException("M195_SIGN_MACHINE_TRUST_NOT_RETAINED_" + store);
        }
        return new Signed(verified.UnsignedMainCatalog,
            finalSha, thumb, subject, proofPath, true);
    }

    private static void VerifyAllMembers(M195LiveSigningPreflight.Verified verified)
    {
        string[] files = Directory.GetFiles(verified.PackageRoot, "*",
            SearchOption.AllDirectories);
        if (verified.ExpectedMemberHashes.Count != 194 || files.Length != 195)
            throw new InvalidDataException("M195_SIGN_FILE_COUNT_CHANGED");
        int observed = 0;
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(verified.PackageRoot, file)
                .Replace('\\', '/');
            Amd26111SourceInventory.ValidateRelative(relative);
            if (relative.Equals("u0206030.cat", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!verified.ExpectedMemberHashes.TryGetValue(relative, out string? sha) ||
                sha != FileSha(file))
                throw new InvalidDataException("M195_SIGN_DRIVER_MEMBER_MUTATED: " + relative);
            observed++;
        }
        if (observed != 194)
            throw new InvalidDataException("M195_SIGN_DRIVER_MEMBERS_MISSING");
    }

    private static string FileSha(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private const string Script = """
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
function Decode([string]$inputText){
    [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($inputText))
}
$cat=Decode '@CAT@'; $inf=Decode '@INF@'; $tool=Decode '@TOOL@'; $work=Decode '@WORK@'
$proof=Join-Path $work 'M195-Live-Signer-Proof.json'
$thumb=$null; $signExit=1; $paExit=1; $infExit=1
$rootAdded=$false; $publisherAdded=$false
$failure=$null; $phase='Start'
function Invoke-SignTool([string[]]$argv,[string]$logName){
    $out=Join-Path $work $logName
    $output=@(& $tool @argv 2>&1 | ForEach-Object {[string]$_})
    $exitCode=[int]$LASTEXITCODE
    [IO.File]::WriteAllLines($out,[string[]]$output,[Text.UTF8Encoding]::new($false))
    return $exitCode
}
function Add-Cert([System.Security.Cryptography.X509Certificates.X509Certificate2]$public,[string]$name){
    $store=[System.Security.Cryptography.X509Certificates.X509Store]::new($name,[System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    try {$store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite);$store.Add($public)}
    finally {$store.Close();$store.Dispose()}
}
function Remove-Cert([string]$name,[string]$id,[string]$expectedSubject){
    $store=[System.Security.Cryptography.X509Certificates.X509Store]::new($name,[System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        foreach($item in @($store.Certificates.Find([System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,$id,$false))){
            if($item.Subject -cne $expectedSubject){throw 'Signer identity mismatch'}
            $store.Remove($item)
        }
    } finally {$store.Close();$store.Dispose()}
}
$subject='CN=Finesse-Local-Live-'+[guid]::NewGuid().ToString('N')
try {
    $phase='Create retained private signer'
    $cert=New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation 'Cert:\LocalMachine\My' -KeyAlgorithm RSA -KeyLength 3072 -KeyExportPolicy NonExportable -HashAlgorithm SHA256 -NotAfter (Get-Date).AddMonths(6)
    if($null -eq $cert -or -not $cert.HasPrivateKey -or $cert.Subject -cne $subject){throw 'Signer creation failed'}
    $thumb=$cert.Thumbprint.ToUpperInvariant()
    if($thumb -notmatch '^[A-F0-9]{40}$'){throw 'Invalid signer thumbprint'}
    $public=[System.Security.Cryptography.X509Certificates.X509Certificate2]::new([byte[]]$cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    try {
        $phase='Add public trust for installation'
        Add-Cert $public 'Root'; $rootAdded=$true
        Add-Cert $public 'TrustedPublisher'; $publisherAdded=$true
    } finally {$public.Dispose()}
    $phase='Sign catalog'
    $signExit=Invoke-SignTool @('sign','/v','/fd','SHA256','/sha1',$thumb,'/sm','/s','My',$cat) 'M195-SignTool-Sign.txt'
    if($signExit -ne 0){throw 'SignTool sign failed'}
    $phase='Verify /pa'
    $paExit=Invoke-SignTool @('verify','/pa','/v',$cat) 'M195-SignTool-VerifyPA.txt'
    if($paExit -ne 0){throw 'SignTool /pa failed'}
    $phase='Verify INF membership'
    $infExit=Invoke-SignTool @('verify','/pa','/v','/c',$cat,$inf) 'M195-SignTool-VerifyINF.txt'
    if($infExit -ne 0){throw 'SignTool INF membership failed'}
} catch {$failure='Phase '+$phase+': '+$_.Exception.ToString()}
finally {
    if($null -ne $failure -and $null -ne $thumb){
        try {if($publisherAdded){Remove-Cert 'TrustedPublisher' $thumb $subject}}
        catch {$failure+='; Publisher cleanup: '+$_.Exception.Message}
        try {if($rootAdded){Remove-Cert 'Root' $thumb $subject}}
        catch {$failure+='; Root cleanup: '+$_.Exception.Message}
        try {Remove-Cert 'My' $thumb $subject}
        catch {$failure+='; Signer cleanup: '+$_.Exception.Message}
    }
    $passed=$null -eq $failure -and $signExit -eq 0 -and $paExit -eq 0 -and $infExit -eq 0
    [ordered]@{
        Schema='Finesse.M195.LiveSigning.v1'
        Status=if($passed){'PASS_INSTALLATION_SIGNER_RETAINED'}else{'FAIL_LIVE_SIGNING'}
        Thumbprint=$thumb
        Subject=$subject
        SignExitCode=$signExit
        PaExitCode=$paExit
        InfExitCode=$infExit
        TrustRetained=$passed
        Failure=$failure
        CatalogSha256=if(Test-Path -LiteralPath $cat){(Get-FileHash -LiteralPath $cat -Algorithm SHA256).Hash}else{$null}
    }|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $proof -Encoding UTF8
}
if($null -ne $failure){throw $failure}
Write-Output 'PASS_INSTALLATION_SIGNER_RETAINED'
""";
}
