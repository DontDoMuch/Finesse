using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Exact current-user Microsoft Store AMD Settings identities from frozen
/// M141. No wildcard AppX removal, no all-users removal, no GPU mutations.
/// Store cleanup failure is evidence/warning, not a blocker to authentic MSI.
/// </summary>
internal static class M195WindowsStoreAmdSettingsCleanup
{
    internal sealed record Outcome(string Status, int? ExitCode,
        string EvidenceFile, int Removed, bool RemovalVerified);

    internal static async Task<Outcome> RemoveCurrentUserAsync(
        string evidenceFolder, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AppX inspection requires Windows.");
        string evidence = Path.Combine(evidenceFolder,
            "M195-Store-AmdSoftware-Cleanup.json");
        if (File.Exists(evidence))
            throw new IOException("M195_APPX_CLEANUP_ALREADY_RECORDED");
        const string script = """
            $ErrorActionPreference='Stop'
            $names=@('AdvancedMicroDevicesInc-2.AMDRadeonSoftware','AdvancedMicroDevicesInc-2.AMDSoftware')
            $removed=0
            foreach($name in $names) {
              $packages=@(Get-AppxPackage -Name $name -ErrorAction Stop)
              foreach($pkg in $packages) {
                if([string]$pkg.Name -cne $name) {continue}
                $fullName=[string]$pkg.PackageFullName
                if([string]::IsNullOrWhiteSpace($fullName)) {throw 'AMD Store package identity missing'}
                Remove-AppxPackage -Package $fullName -ErrorAction Stop
                $remaining=@(Get-AppxPackage -Name $name -ErrorAction Stop |
                  Where-Object { $_.PackageFullName -eq $fullName })
                if($remaining.Count -ne 0) {throw 'AMD Store package still installed'}
                Write-Output ('M195_STORE_REMOVED='+$fullName)
                $removed++
              }
            }
            Write-Output ('M195_STORE_REMOVED_COUNT='+$removed)
            """;
        string powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        int? exit = null;
        string stdout = string.Empty, stderr = string.Empty;
        string status = "STORE_CLEANUP_WARNING_CONTINUE_MSI";
        int removed = 0;
        bool verified = false;
        try
        {
            if (!File.Exists(powershell))
                throw new FileNotFoundException("Windows PowerShell 5.1 missing.", powershell);
            var start = new ProcessStartInfo(powershell) {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (string arg in new[] {
                "-NoLogo", "-NoProfile", "-NonInteractive",
                "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
                start.ArgumentList.Add(arg);
            using Process child = Process.Start(start) ??
                throw new IOException("M195_APPX_POWERSHELL_DID_NOT_START");
            using var limited = CancellationTokenSource.CreateLinkedTokenSource(token);
            limited.CancelAfter(TimeSpan.FromMinutes(5));
            try
            {
                Task<string> o = child.StandardOutput.ReadToEndAsync(limited.Token);
                Task<string> e = child.StandardError.ReadToEndAsync(limited.Token);
                await Task.WhenAll(o, e, child.WaitForExitAsync(limited.Token));
                stdout = o.Result; stderr = e.Result;
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
            exit = child.ExitCode;
            if (exit == 0)
            {
                var markers = stdout.Split('\n').Select(line => line.Trim());
                string[] counts = markers.Where(line =>
                    line.StartsWith("M195_STORE_REMOVED_COUNT=", StringComparison.Ordinal)).ToArray();
                if (counts.Length != 1 ||
                    !int.TryParse(counts[0]["M195_STORE_REMOVED_COUNT=".Length..], out removed) ||
                    removed < 0)
                    throw new InvalidDataException("M195_APPX_SUCCESS_MARKER_MISSING");
                int actual = markers.Count(line =>
                    line.StartsWith("M195_STORE_REMOVED=", StringComparison.Ordinal));
                if (actual != removed)
                    throw new InvalidDataException("M195_APPX_COUNT_MISMATCH");
                verified = true;
                status = removed == 0 ? "STORE_AMD_NOT_PRESENT" : "STORE_AMD_EXACT_PACKAGES_REMOVED";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // User cancellation must not be silently relabeled as successful.
            throw;
        }
        catch (Exception ex)
        {
            stderr += "\n" + ex.GetType().Name + ": " + ex.Message;
        }
        await File.WriteAllTextAsync(evidence,
            JsonSerializer.Serialize(new {
                Schema = "Finesse.M195.StoreAmdSoftwareCleanup.v1",
                Status = status, ExitCode = exit, Removed = removed,
                Verified = verified,
                Stdout = stdout[..Math.Min(stdout.Length, 40_000)],
                Stderr = stderr[..Math.Min(stderr.Length, 40_000)],
                UnrelatedStorePackagesRemoved = false,
                WindowsRebootRequested = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false), CancellationToken.None);
        return new Outcome(status, exit, evidence, removed, verified);
    }
}
