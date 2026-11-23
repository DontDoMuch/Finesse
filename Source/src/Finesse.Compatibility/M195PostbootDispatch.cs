namespace Finesse.Compatibility;

/// <summary>
/// Single-file Finesse.exe startup hook to be called BEFORE starting the GUI.
/// This is internal source integration only: none of the current public tool
/// entrypoints invoke it. The future product host must explicitly call
/// TryHandleAsync(args) at process startup.
/// </summary>
internal static class M195PostbootDispatch
{
    internal static bool IsExactResumeCommand(IReadOnlyList<string>? args) =>
        args is { Count: 1 } &&
        string.Equals(args[0], M195PostbootTicketContract.ResumeArgument,
            StringComparison.Ordinal);

    /// <returns>
    /// Null for normal GUI startup, otherwise a concrete postboot process exit
    /// code. Returning null must NEVER start an installation automatically.
    /// </returns>
    internal static async Task<int?> TryHandleAsync(
        IReadOnlyList<string> args, CancellationToken token = default)
    {
        if (!IsExactResumeCommand(args))
            return null;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Postboot resume requires Windows.");
        string host = Environment.ProcessPath ??
            throw new InvalidDataException("M195_POSTBOOT_RUNNING_EXE_UNAVAILABLE");
        M195PostbootTicketContract.RequireFinesseHostPath(host);

        var outcome = await M195WindowsPostbootActions.ResumeAsync(host, token);
        // Only the exact target driver Code0 + verified AMD Software may be
        // represented by zero. A recovered baseline is NOT target success.
        return outcome.Status switch
        {
            M195LifecycleSequence.PostbootState.TargetConfirmedCode0 => 0,
            M195LifecycleSequence.PostbootState.TargetFailedRecovered => 20,
            M195LifecycleSequence.PostbootState.TargetFailedRecoveryUnverified => 21,
            _ => throw new InvalidDataException("M195_POSTBOOT_UNKNOWN_FINAL_OUTCOME")
        };
    }
}
