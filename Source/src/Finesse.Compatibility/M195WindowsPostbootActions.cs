using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// M191/M182 first-observation-before-recovery port. This is internal and
/// not called by any GUI or startup route until the completed EXE has a
/// verified --m195-postboot dispatch. No automatic reboot here.
/// </summary>
internal sealed class M195WindowsPostbootActions : M195LifecycleSequence.IPostbootActions
{
    private readonly M195WindowsPostbootRegistration.DurableState _state;
    private readonly string _reports;
    private string? _firstFile;
    private M195LifecycleSequence.PostbootObservation? _first;
    private M195WindowsGpuInventory.ExactGpu? _observed;
    private string? _inventoryError;

    internal M195WindowsPostbootActions(
        M195WindowsPostbootRegistration.DurableState state,
        string reports)
    {
        ArgumentNullException.ThrowIfNull(state);
        M195PostbootTicketContract.RequireValid(state.Pending);
        if (!state.TaskReadbackVerified)
            throw new InvalidDataException("M195_STEP12_UNREGISTERED_POSTBOOT_STATE");
        _state = state;
        _reports = Path.GetFullPath(reports);
    }

    internal static async Task<M195LifecycleSequence.PostbootOutcome> ResumeAsync(
        string executingFinesseExe, CancellationToken token = default)
    {
        // The staged SHA-verified executable and exact SID/state are checked
        // BEFORE any Windows PnP or software-state observation.
        var state = await M195WindowsPostbootRegistration.ReadStagedStateAsync(
            executingFinesseExe, token);
        var folder = Path.GetDirectoryName(Path.GetFullPath(
            executingFinesseExe)) ??
            throw new InvalidDataException("M195_STEP12_STAGED_DIRECTORY_MISSING");
        string reports = Path.Combine(folder, "Reports");
        Directory.CreateDirectory(reports);
        if (File.Exists(Path.Combine(reports, "M195-Postboot-Outcome.json")))
            throw new InvalidDataException("M195_STEP12_ALREADY_COMPLETED");
        var native = new M195WindowsPostbootActions(state, reports);
        M195LifecycleSequence.PostbootOutcome outcome;
        try
        {
            outcome = await M195LifecycleSequence.RunPostbootAsync(
                state.Pending, native, token: token);
            await M195WindowsPostbootRegistration.WriteNewAsync(
                Path.Combine(reports, "M195-Postboot-Outcome.json"), new {
                    Schema = "Finesse.M195.PostbootOutcome.v1",
                    RecordedUtc = DateTimeOffset.UtcNow, Outcome = outcome,
                    FirstEvidenceFile = native._firstFile,
                    RecoveryAttempted = outcome.Recovery is not null,
                    NoAdditionalRebootRequested = true
                }, CancellationToken.None);
        }
        catch (Exception error)
        {
            try
            {
                await M195WindowsPostbootRegistration.WriteNewAsync(
                    Path.Combine(reports, "M195-Postboot-Failure-" +
                        Guid.NewGuid().ToString("N") + ".json"), new {
                        Schema = "Finesse.M195.PostbootError.v1",
                        RecordedUtc = DateTimeOffset.UtcNow,
                        Failure = error.ToString(),
                        FirstEvidenceFile = native._firstFile,
                        FirstObservation = native._first,
                        NoAdditionalRebootRequested = true
                    }, CancellationToken.None);
            }
            catch { /* preserve original failure */ }
            throw;
        }
        // The one-shot task is removed AFTER the original first observation,
        // postboot result and any recovery evidence have been durably written.
        // Cleanup never edits the signed driver or requests a reboot.
        try { await native.UnregisterTaskAsync(CancellationToken.None); }
        catch (Exception error)
        {
            await M195WindowsPostbootRegistration.WriteNewAsync(
                Path.Combine(reports, "M195-Postboot-CleanupWarning.json"), new {
                    Schema = "Finesse.M195.PostbootCleanupWarning.v1",
                    Warning = error.ToString(), TaskName = state.TaskName
                }, CancellationToken.None);
        }
        return outcome;
    }

    public async Task<M195LifecycleSequence.PostbootObservation>
        CaptureFirstObservationAsync(
            M195LifecycleSequence.PendingPostboot pending, CancellationToken token)
    {
        if (pending != _state.Pending || _first is not null)
            throw new InvalidDataException("M195_STEP12_FIRST_CAPTURE_WRONG_TRANSACTION");
        token.ThrowIfCancellationRequested();
        string boot = M195PostbootTicketContract.CurrentBootUtc();
        IReadOnlyList<M195WindowsGpuInventory.ObservedDisplay> inventory = [];
        try
        {
            inventory = await M195WindowsGpuInventory.EnumerateAsync(token);
            _observed = M195WindowsGpuInventory.RequireUniqueExactGpu(inventory);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _inventoryError = error.GetType().Name + ": " + error.Message;
        }
        bool software = M195WindowsAmdSettingsMsiIdentity.ExactTargetInstalled() &&
            File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                M195AmdSettings26111.NativeFrontendRelative.Replace('/',
                    Path.DirectorySeparatorChar)));
        var first = new M195LifecycleSequence.PostbootObservation(
            _observed?.Display.InstanceId ?? "",
            _observed?.Display.PublishedInf ?? "",
            _observed?.Display.Version ?? "",
            checked((int)(_observed?.Display.ProblemCode ?? 0xFFFFFFFFu)),
            software, boot);
        string evidence = Path.Combine(_reports, "M195-FirstPostboot-" +
            Guid.NewGuid().ToString("N") + ".json");
        // The first snapshot is immutable. Do not rewrite the same file after
        // rollback; keep separate recovery and outcome receipts.
        await M195WindowsPostbootRegistration.WriteNewAsync(evidence, new {
            Schema = "Finesse.M195.M191.FirstPostbootSnapshot.v1",
            TimestampUtc = DateTimeOffset.UtcNow,
            TransactionId = pending.TransactionId,
            OriginalBootUtc = pending.RegisteredBootIdentity,
            FirstObservedBootUtc = boot,
            ActuallyRebooted = M195PostbootTicketContract.IsNewBoot(
                pending.RegisteredBootIdentity, boot),
            FirstGpu = first,
            FirstService = _observed?.Display.Service,
            FirstDeviceNodeStatus = _observed?.Display.DevNodeStatus,
            FirstHardwareIds = _observed?.Display.HardwareIds,
            InventoryError = _inventoryError,
            ExpectedProfile = pending.ExactHardwareId,
            ExpectedDriverInf = pending.ExpectedPublishedInf,
            ExpectedDriverVersion = pending.ExpectedDriverVersion,
            OriginalInf = pending.OriginalPublishedInf,
            OriginalVersion = pending.OriginalDriverVersion,
            RecoveryAttempted = false,
            RestartRequested = false,
            GpuEnumerationCount = inventory.Count
        }, CancellationToken.None);
        _firstFile = evidence;
        _first = first;
        return first;
    }

    public async Task<M195LifecycleSequence.RecoveryResult> RecoverBaselineAsync(
        M195LifecycleSequence.PendingPostboot pending,
        M195LifecycleSequence.PostbootObservation first, CancellationToken token)
    {
        if (pending != _state.Pending || _first is null || first != _first ||
            _firstFile is null || !File.Exists(_firstFile) ||
            !M195PostbootTicketContract.IsNewBoot(
                pending.RegisteredBootIdentity, first.BootIdentity))
            throw new InvalidDataException("M195_STEP12_NO_IMMUTABLE_FIRST_REBOOT_SNAPSHOT");
        token.ThrowIfCancellationRequested();
        string status = "RECOVERY_NOT_STARTED";
        bool restored = false;
        string? restoredInf = null;
        int? problemCode = null;
        object? nativeReceipt = null;
        try
        {
            var current = M195WindowsGpuInventory.RequireUniqueExactGpu(
                await M195WindowsGpuInventory.EnumerateAsync(token));
            if (current.Profile != HandheldProfiles.Resolve(pending.ExactHardwareId) ||
                !string.Equals(current.Display.InstanceId, pending.InstanceId,
                    StringComparison.OrdinalIgnoreCase))
            {
                status = "RECOVERY_PHYSICAL_GPU_CHANGED_NO_BIND";
            }
            else if (current.Display.ProblemCode == 0 &&
                     string.Equals(current.Display.PublishedInf,
                         pending.OriginalPublishedInf,
                         StringComparison.OrdinalIgnoreCase) &&
                     current.Display.Version == pending.OriginalDriverVersion)
            {
                status = "BASELINE_ALREADY_HEALTHY_CODE0";
                restored = true;
                restoredInf = current.Display.PublishedInf;
                problemCode = 0;
            }
            else
            {
                string? hardwareId = current.Display.HardwareIds.FirstOrDefault(h =>
                    string.Equals(h, pending.ExactHardwareId,
                        StringComparison.OrdinalIgnoreCase));
                if (hardwareId is null &&
                    current.Profile == HandheldProfiles.Go1)
                {
                    string revisionless = pending.ExactHardwareId.Split(
                        "&REV_", StringSplitOptions.None)[0];
                    hardwareId = current.Display.HardwareIds.FirstOrDefault(h =>
                        string.Equals(h, revisionless,
                            StringComparison.OrdinalIgnoreCase));
                }
                if (hardwareId is null)
                {
                    status = "RECOVERY_BIND_HARDWARE_ID_NOT_PRESENT";
                }
                else
                {
                    var recovery = await M195WindowsNativeBind
                        .RestoreAfterPostbootFailureAsync(pending, hardwareId, current);
                    nativeReceipt = recovery;
                    status = recovery.Status;
                    restored = recovery.ConfirmedCode0;
                    restoredInf = recovery.ObservedInf;
                    problemCode = recovery.ObservedProblemCode is null
                        ? null : checked((int)recovery.ObservedProblemCode.Value);
                }
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            status = "RECOVERY_EXCEPTION_UNVERIFIED:" +
                error.GetType().Name + ": " + error.Message;
        }
        await M195WindowsPostbootRegistration.WriteNewAsync(
            Path.Combine(_reports, "M195-Postboot-Recovery.json"), new {
                Schema = "Finesse.M195.PostbootRecovery.v1",
                Status = status, FirstEvidenceFile = _firstFile,
                FirstGpu = first, RecoveryNativeResult = nativeReceipt,
                OriginalInf = pending.OriginalPublishedInf,
                OriginalDriverVersion = pending.OriginalDriverVersion,
                RestoredAndCode0 = restored, ObservedInf = restoredInf,
                ObservedProblemCode = problemCode,
                ExtraRestartRequested = false,
                TimestampUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);
        return new(restored, restoredInf, problemCode);
    }

    private async Task UnregisterTaskAsync(CancellationToken token)
    {
        string ps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        static string Enc(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        string script = """
            $ErrorActionPreference='Stop'
            function Decode([string]$x) {
              [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($x))
            }
            $taskName=Decode('@TASK@')
            $exe=Decode('@EXE@')
            $args=Decode('@ARGS@')
            $sid=Decode('@SID@')
            $task=Get-ScheduledTask -TaskName $taskName -ErrorAction Stop
            if(@($task.Actions).Count -ne 1 -or
               [string]$task.Actions[0].Execute -ine $exe -or
               [string]$task.Actions[0].Arguments -cne $args -or
               [string]$task.Principal.RunLevel -notin @('Highest','1')) {
              throw 'M195_STEP12_TASK_CLEANUP_IDENTITY_MISMATCH'
            }
            $taskSid=[string]$task.Principal.UserId
            if($taskSid -notmatch '^S-1-\d+(?:-\d+)+$') {
              $taskSid=([Security.Principal.NTAccount]::new($taskSid)).Translate(
                [Security.Principal.SecurityIdentifier]).Value
            }
            if($taskSid -ne $sid) {throw 'M195_STEP12_TASK_CLEANUP_PRINCIPAL_CHANGED'}
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction Stop
            """;
        script = script.Replace("@TASK@", Enc(_state.TaskName), StringComparison.Ordinal)
            .Replace("@EXE@", Enc(_state.StagedExecutablePath), StringComparison.Ordinal)
            .Replace("@ARGS@", Enc(M195PostbootTicketContract.ResumeArgument), StringComparison.Ordinal)
            .Replace("@SID@", Enc(_state.RegisteredPrincipalSid), StringComparison.Ordinal);
        var start = new ProcessStartInfo(ps) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string arg in new[] {
            "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            start.ArgumentList.Add(arg);
        using Process child = Process.Start(start) ??
            throw new IOException("M195_STEP12_TASK_CLEANUP_DID_NOT_START");
        var output = child.StandardOutput.ReadToEndAsync(token);
        var errors = child.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(output, errors, child.WaitForExitAsync(token));
        if (child.ExitCode != 0)
            throw new InvalidDataException(
                "M195_STEP12_TASK_CLEANUP_FAILED_" + child.ExitCode + ":" +
                errors.Result[..Math.Min(1024, errors.Result.Length)]);
    }
}
