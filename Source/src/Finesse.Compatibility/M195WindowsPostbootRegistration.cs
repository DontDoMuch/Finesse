using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Frozen M182 / M155 postboot one-shot registration. Internal only, NOT
/// invoked by the public GUI. No restart occurs during RegisterAsync.
/// </summary>
internal static class M195WindowsPostbootRegistration
{
    internal sealed record DurableState(
        M195LifecycleSequence.PendingPostboot Pending,
        string ExecutableSha256, string StagedExecutablePath,
        string TaskName, string RegisteredPrincipalSid,
        string RegisteredAtUtc, bool TaskReadbackVerified);

    internal sealed record Registration(
        M195LifecycleSequence.RegisteredPostboot Result,
        string TaskName, string StagedExecutable, string StateFile,
        string ReceiptFile, string ExecutableSha256);

    internal static async Task<Registration> RegisterAsync(
        M195LifecycleSequence.PendingPostboot pending,
        M195LifecycleSequence.AssignedDriver original,
        M195LifecycleSequence.Baseline baseline,
        string evidenceRoot, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Step 11 requires Windows.");
        M195PostbootTicketContract.RequireValid(pending);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(baseline);
        if (pending.OriginalPublishedInf != original.PublishedInf ||
            pending.OriginalDriverVersion != original.Version ||
            (baseline.Verified &&
             (!string.Equals(pending.VerifiedBackupDirectory,
                 baseline.VerifiedExportDirectory, StringComparison.OrdinalIgnoreCase) ||
              !string.Equals(baseline.RecoveryInf, original.PublishedInf,
                 StringComparison.OrdinalIgnoreCase))) ||
            (!baseline.Verified && pending.VerifiedBackupDirectory is not null))
            throw new InvalidDataException("M195_STEP11_ORIGINAL_DRIVER_OR_BACKUP_TICKET_DRIFT");
        using (var user = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(user).IsInRole(WindowsBuiltInRole.Administrator))
                throw new UnauthorizedAccessException("M195_STEP11_ADMIN_REQUIRED");
        }
        string sid = WindowsIdentity.GetCurrent().User?.Value ??
            throw new InvalidDataException("M195_STEP11_USER_SID_UNAVAILABLE");
        string running = Environment.ProcessPath ??
            throw new InvalidDataException("M195_STEP11_CURRENT_EXECUTABLE_UNKNOWN");
        running = Path.GetFullPath(running);
        RequireNoReparse(running);
        if (!File.Exists(running) ||
            !string.Equals(Path.GetExtension(running), ".exe",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_STEP11_CURRENT_EXE_REQUIRED");
        string sha = Hash(running);
        string root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Finesse", "Postboot", sha, pending.TransactionId);
        RequireNoReparse(Path.GetDirectoryName(root)!);
        if (Directory.Exists(root) || File.Exists(root))
            throw new IOException("M195_STEP11_TRANSACTION_ALREADY_STAGED");
        string task = M195PostbootTicketContract.TaskName(pending.TransactionId);
        string evidenceFolder = Path.GetFullPath(evidenceRoot);
        RequireNoReparse(Path.GetDirectoryName(evidenceFolder)!);
        if (Directory.Exists(evidenceFolder) || File.Exists(evidenceFolder))
            throw new IOException("M195_STEP11_EVIDENCE_ALREADY_EXISTS");
        Directory.CreateDirectory(evidenceFolder);
        Directory.CreateDirectory(root);
        string stagedExe = Path.Combine(root, M195PostbootTicketContract.ExecutableName);
        string stateFile = Path.Combine(root, M195PostbootTicketContract.StateFileName);
        string receiptFile = Path.Combine(evidenceFolder, "M195-Step11-Registration.json");
        string status = "STAGING";
        string stdout = "", stderr = "";
        int? exit = null;
        try
        {
            File.Copy(running, stagedExe, overwrite: false);
            if (!string.Equals(Hash(stagedExe), sha, StringComparison.Ordinal))
                throw new InvalidDataException("M195_STEP11_STAGED_EXECUTABLE_SHA_MISMATCH");
            var state = new DurableState(pending, sha, stagedExe, task,
                sid, DateTimeOffset.UtcNow.ToString("O"), true);
            await WriteNewAsync(stateFile, state, token);
            if (!File.Exists(stateFile))
                throw new InvalidDataException("M195_STEP11_PENDING_TICKET_NOT_DURABLE");
            var reread = JsonSerializer.Deserialize<DurableState>(
                await File.ReadAllTextAsync(stateFile, token));
            if (reread is null || reread.Pending != pending ||
                reread.ExecutableSha256 != sha ||
                reread.TaskName != task || reread.RegisteredPrincipalSid != sid)
                throw new InvalidDataException("M195_STEP11_TICKET_READBACK_CHANGED");
            status = "REGISTERING_TASK";
            var executed = await RunPowerShellAsync(BuildRegistrationScript(
                stagedExe, M195PostbootTicketContract.ResumeArgument, task, sid),
                TimeSpan.FromMinutes(3), token);
            exit = executed.ExitCode;
            stdout = executed.Stdout;
            stderr = executed.Stderr;
            if (exit != 0 ||
                !stdout.Contains("M195_POSTBOOT_TASK_VERIFIED=", StringComparison.Ordinal))
                throw new InvalidDataException("M195_STEP11_TASK_NOT_VERIFIED");
            status = "REGISTERED_PENDING_SINGLE_RESTART";
            await WriteNewAsync(receiptFile, new {
                Schema = "Finesse.M195.Step11Registration.v1",
                Status = status, Pending = pending, TaskName = task,
                StagedExecutable = stagedExe, ExecutableSha256 = sha,
                StateFile = stateFile, PrincipalSid = sid, TaskExitCode = exit,
                TaskStdout = stdout[..Math.Min(stdout.Length, 20000)],
                TaskStderr = stderr[..Math.Min(stderr.Length, 20000)],
                RestartRequested = false, AmdSoftwareVerifiedBeforeRegistration = true,
                FirstPostbootObservationCaptured = false
            }, CancellationToken.None);
            return new Registration(new(pending.TransactionId, true),
                task, stagedExe, stateFile, receiptFile, sha);
        }
        catch (Exception error)
        {
            status = "FAILED_OR_INCOMPLETE_NO_RESTART";
            try
            {
                await WriteNewAsync(Path.Combine(evidenceFolder,
                    "M195-Step11-Registration-Failure.json"), new {
                    Schema = "Finesse.M195.Step11RegistrationFailure.v1",
                    Status = status, Failure = error.ToString(),
                    TaskName = task, StateFile = stateFile,
                    RegistrationExitCode = exit,
                    TaskStdout = stdout[..Math.Min(stdout.Length, 20000)],
                    TaskStderr = stderr[..Math.Min(stderr.Length, 20000)],
                    RestartRequested = false
                }, CancellationToken.None);
            }
            catch { /* never hide the original failure */ }
            throw;
        }
    }

    internal static async Task RequestSingleRestartAsync(
        Registration registered, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Restart requires Windows.");
        ArgumentNullException.ThrowIfNull(registered);
        if (!registered.Result.RegistrationVerified ||
            !File.Exists(registered.StateFile) ||
            !File.Exists(registered.StagedExecutable) ||
            Hash(registered.StagedExecutable) != registered.ExecutableSha256)
            throw new InvalidDataException("M195_STEP11_REGISTERED_BINARY_NOT_VERIFIED");
        string ledger = Path.Combine(
            Path.GetDirectoryName(registered.ReceiptFile)!,
            "M195-Step11-SingleRestartRequest.json");
        // CreateNew means a second request in the same transaction is forbidden,
        // including when Windows' response to the first request was uncertain.
        await WriteNewAsync(ledger, new {
            Schema = "Finesse.M195.OneRestartRequest.v1",
            RegisteredTransaction = registered.Result.TransactionId,
            CreatedUtc = DateTimeOffset.UtcNow,
            TaskName = registered.TaskName, RestartCommand = "shutdown /r /t 10",
            AcceptedByWindows = (bool?)null, MayAlreadyBeScheduled = true
        }, token);
        string shutdown = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "shutdown.exe");
        using var process = Process.Start(new ProcessStartInfo(shutdown) {
            UseShellExecute = false, CreateNoWindow = true,
            ArgumentList = { "/r", "/t", "10",
                "/c", "Finesse: restarting once to verify AMD Software and your GPU." }
        }) ?? throw new IOException("M195_STEP11_SHUTDOWN_DID_NOT_START");
        await process.WaitForExitAsync(token);
        if (process.ExitCode != 0)
            throw new IOException("M195_STEP11_SHUTDOWN_REJECTED_" + process.ExitCode);
        string accepted = ledger + ".accepted";
        await WriteNewAsync(accepted, new {
            Schema = "Finesse.M195.RestartAccepted.v1",
            RegisteredTransaction = registered.Result.TransactionId,
            AcceptedUtc = DateTimeOffset.UtcNow,
            ShutdownExitCode = process.ExitCode
        }, CancellationToken.None);
    }

    internal static async Task<DurableState> ReadStagedStateAsync(
        string runningExecutable, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Postboot state requires Windows.");
        string exe = Path.GetFullPath(runningExecutable);
        RequireNoReparse(exe);
        string directory = Path.GetDirectoryName(exe) ??
            throw new InvalidDataException("M195_STEP12_EXE_DIRECTORY_MISSING");
        string root = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Finesse", "Postboot")) + Path.DirectorySeparatorChar;
        if (!exe.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(exe) != M195PostbootTicketContract.ExecutableName)
            throw new InvalidDataException("M195_STEP12_NOT_STAGED_EXECUTABLE");
        string statePath = Path.Combine(directory,
            M195PostbootTicketContract.StateFileName);
        RequireNoReparse(statePath);
        var state = JsonSerializer.Deserialize<DurableState>(
            await File.ReadAllTextAsync(statePath, token)) ??
            throw new InvalidDataException("M195_STEP12_STATE_EMPTY");
        M195PostbootTicketContract.RequireValid(state.Pending);
        string sid = WindowsIdentity.GetCurrent().User?.Value ??
            throw new InvalidDataException("M195_STEP12_NO_USER_SID");
        if (!state.TaskReadbackVerified ||
            state.TaskName != M195PostbootTicketContract.TaskName(state.Pending.TransactionId) ||
            !state.StagedExecutablePath.Equals(exe, StringComparison.OrdinalIgnoreCase) ||
            !state.RegisteredPrincipalSid.Equals(sid, StringComparison.Ordinal) ||
            !M195PostbootTicketContract.IsSha(state.ExecutableSha256) ||
            !string.Equals(Hash(exe), state.ExecutableSha256,
                StringComparison.Ordinal))
            throw new InvalidDataException("M195_STEP12_BINARY_OR_TASK_IDENTITY_CHANGED");
        return state;
    }

    internal static async Task WriteNewAsync(string path, object data,
        CancellationToken token)
    {
        using var file = new FileStream(path, FileMode.CreateNew,
            FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(file, data,
            new JsonSerializerOptions { WriteIndented = true }, token);
        await file.FlushAsync(token);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunPowerShellAsync(
        string script, TimeSpan timeout, CancellationToken token)
    {
        string ps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var info = new ProcessStartInfo(ps) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in new[] {
            "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ??
            throw new IOException("M195_STEP11_POWERSHELL_DID_NOT_START");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var errors = process.StandardError.ReadToEndAsync(limit.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(limit.Token));
            return (process.ExitCode, output.Result, errors.Result);
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

    private static string BuildRegistrationScript(
        string exe, string args, string task, string sid)
    {
        static string Enc(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        const string script = """
            $ErrorActionPreference='Stop'
            function Decode([string]$value) {
              [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($value))
            }
            $exe=Decode('@EXE@')
            $arguments=Decode('@ARGS@')
            $taskName=Decode('@TASK@')
            $expectedSid=Decode('@SID@')
            $sessionId=(Get-Process -Id $PID).SessionId
            $shellSids=@()
            try {
              $shellSids=@(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -ErrorAction Stop |
                Where-Object { $_.SessionId -eq $sessionId } | ForEach-Object {
                  $owner=Invoke-CimMethod -InputObject $_ -MethodName GetOwnerSid -ErrorAction Stop
                  if($owner.ReturnValue -eq 0 -and $owner.Sid) {[string]$owner.Sid}
                } | Sort-Object -Unique)
            } catch { $shellSids=@() }
            if($shellSids.Count -gt 1) {throw 'M195_STEP11_DESKTOP_IDENTITY_AMBIGUOUS'}
            $sid=$expectedSid
            if($shellSids.Count -eq 1) {$sid=[string]$shellSids[0]}
            if($sid -ne $expectedSid -or
               [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne $expectedSid) {
              throw 'M195_STEP11_INTERACTIVE_USER_DIFFERS_FROM_ELEVATED_ACCOUNT'
            }
            $already=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
            if($already) {throw 'M195_STEP11_TASK_ALREADY_EXISTS'}
            $action=New-ScheduledTaskAction -Execute $exe -Argument $arguments -WorkingDirectory (Split-Path -Parent $exe)
            $trigger=New-ScheduledTaskTrigger -AtLogOn
            $principal=New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Highest
            $settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Minutes 20)
            Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -ErrorAction Stop | Out-Null
            $r=Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
            if(@($r.Actions).Count -ne 1 -or @($r.Triggers).Count -ne 1 -or
               [string]$r.Actions[0].Execute -ine $exe -or
               [string]$r.Actions[0].Arguments -cne $arguments -or
               [string]$r.Principal.RunLevel -notin @('Highest','1') -or
               [string]$r.Principal.LogonType -notin @('Interactive','InteractiveToken','3')) {
               throw 'M195_STEP11_TASK_READBACK_FAILED'
            }
            $readbackSid=[string]$r.Principal.UserId
            if($readbackSid -notmatch '^S-1-\d+(?:-\d+)+$') {
              $readbackSid=([Security.Principal.NTAccount]::new($readbackSid)).Translate(
                [Security.Principal.SecurityIdentifier]).Value
            }
            if($readbackSid -ne $expectedSid) {throw 'M195_STEP11_TASK_PRINCIPAL_DRIFT'}
            Write-Output ('M195_POSTBOOT_TASK_VERIFIED='+$taskName)
            """;
        return script.Replace("@EXE@", Enc(exe), StringComparison.Ordinal)
            .Replace("@ARGS@", Enc(args), StringComparison.Ordinal)
            .Replace("@TASK@", Enc(task), StringComparison.Ordinal)
            .Replace("@SID@", Enc(sid), StringComparison.Ordinal);
    }

    private static string Hash(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f));
    }
    private static void RequireNoReparse(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_POSTBOOT_REPARSE_PATH_REJECTED");
            string? parent = Path.GetDirectoryName(cursor);
            if (string.Equals(cursor, parent, StringComparison.OrdinalIgnoreCase))
                break;
            cursor = parent;
        }
    }
}
