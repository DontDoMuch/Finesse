using System.Globalization;

namespace Finesse.Compatibility;

/// <summary>
/// Frozen M182/M191 one-reboot ticket identity. This is pure and testable
/// without installing, rebooting or observing a GPU.
/// </summary>
public static class M195PostbootTicketContract
{
    public const string ResumeArgument = "--m195-postboot";
    public const string StateFileName = "M195-Postboot-State.json";
    public const string ExecutableName = "Finesse.exe";
    public const string TaskPrefix = "Finesse-M195-Postboot-";

    public static void RequireValid(M195LifecycleSequence.PendingPostboot state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Guid.TryParseExact(state.TransactionId, "N", out Guid id) ||
            !string.Equals(id.ToString("N"), state.TransactionId,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(state.InstanceId) ||
            HandheldProfiles.Resolve(state.ExactHardwareId) is not { } profile ||
            !string.Equals(state.ExpectedDriverVersion,
                OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion,
                StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(state.OriginalDriverVersion) ||
            !IsSha(state.ExpectedInstallerSha256) ||
            !string.Equals(state.ExpectedInstallerSha256,
                OfficialAmd26111SourceContract.VerifiedFrozenM194.InstallerSha256,
                StringComparison.Ordinal) ||
            !IsSha(state.ExpectedMainCatalogSha256) ||
            !IsBootUtc(state.RegisteredBootIdentity))
            throw new InvalidDataException("M195_POSTBOOT_TICKET_IDENTITY_INVALID");
        _ = M195WindowsBindIdentity.RequirePublishedOemInf(state.ExpectedPublishedInf);
        _ = M195WindowsBindIdentity.RequirePublishedOemInf(state.OriginalPublishedInf);
        if (profile != HandheldProfiles.Go1 &&
            string.IsNullOrWhiteSpace(state.VerifiedBackupDirectory))
            throw new InvalidDataException("M195_POSTBOOT_TESTER_VERIFIED_BACKUP_REQUIRED");
    }

    public static bool IsNewBoot(string before, string after)
    {
        if (!DateTimeOffset.TryParseExact(before, "O",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var original) ||
            !DateTimeOffset.TryParseExact(after, "O",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var current))
            return false;
        // The observed Windows boot-time estimate is monotonic over a reboot,
        // with small clock/runtime jitter ignored (M182 threshold: >30 s).
        return (current - original) > TimeSpan.FromSeconds(30);
    }

    public static string TaskName(string transactionId)
    {
        if (!Guid.TryParseExact(transactionId, "N", out var id) ||
            !string.Equals(id.ToString("N"), transactionId, StringComparison.Ordinal))
            throw new InvalidDataException("M195_POSTBOOT_TASK_TRANSACTION_INVALID");
        return TaskPrefix + transactionId;
    }

    public static string CurrentBootUtc()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows boot identity is required.");
        return (DateTimeOffset.UtcNow - TimeSpan.FromMilliseconds(
            Environment.TickCount64)).ToString("O", CultureInfo.InvariantCulture);
    }

    public static bool IsSha(string? hash) =>
        hash is { Length: 64 } && hash.All(Uri.IsHexDigit);

    private static bool IsBootUtc(string value) =>
        DateTimeOffset.TryParseExact(value, "O",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
