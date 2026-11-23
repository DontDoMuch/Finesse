namespace Finesse.Compatibility;

/// <summary>
/// Real 26.11.1 Step 10 integration point for the future full M195 lifecycle.
/// INTERNAL and disconnected from M195WindowsPreparationActions / GUI until
/// Steps 11-12 implement verified postboot and rollback.
/// </summary>
internal static class M195WindowsStep10Native
{
    internal sealed record Result(
        M195LifecycleSequence.SoftwareResult Software,
        M195WindowsAmdSettingsInstall.Outcome Installed,
        M195AmdSettingsMsiPreparation.Prepared Source);

    internal static async Task<Result> ExecuteAsync(
        M195LifecycleSequence.Device device,
        M195LifecycleSequence.BoundDriver assigned,
        OfficialAmdInstallerVerifier.VerifiedSource officialAmd,
        Amd26111ArchiveExtraction.VerifiedExtraction originalExtraction,
        string verified7zr,
        string pinnedPortable7zipRelease,
        string newSoftwareWorkspace,
        string freshEvidenceDirectory,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(assigned);
        ArgumentNullException.ThrowIfNull(officialAmd);
        ArgumentNullException.ThrowIfNull(originalExtraction);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Step 10 requires Windows.");

        // No new installer, uninstall, signing or reboot is allowed if Step 9
        // does not independently identify the exact signed/assigned GPU.
        if (device.Profile != HandheldProfiles.Resolve(device.ExactHardwareId) ||
            !assigned.SignedOnce ||
            assigned.TargetDriverVersion != M195AmdSettings26111.TargetDriverVersion ||
            string.IsNullOrWhiteSpace(assigned.MainSignedCatalogSha256))
            throw new InvalidDataException("M195_STEP10_NOT_THE_VERIFIED_STEP8_DRIVER");

        var observation = await M195WindowsPrebootObservation.ObserveAsync(
            device, assigned, token);
        if (observation.Classification == M195LifecycleSequence.PrebootState.Reject)
            throw new InvalidDataException("M195_STEP10_PREBOOT_EXACT_GPU_NOT_ASSIGNED");

        M195AmdSettingsMsiPreparation.Prepared prepared =
            await M195AmdSettingsMsiPreparation.PrepareAsync(
                officialAmd, originalExtraction, verified7zr,
                pinnedPortable7zipRelease, newSoftwareWorkspace, token);
        M195WindowsAmdSettingsInstall.Outcome outcome =
            await M195WindowsAmdSettingsInstall.ExecuteAsync(
                device, assigned, prepared, freshEvidenceDirectory, token);
        // MSI result 3010 is a deferred reboot request only; never reboot
        // directly here. The future Step 11 owns one planned Windows restart.
        return new Result(new M195LifecycleSequence.SoftwareResult(
            outcome.InstalledAndVerified, outcome.Status, outcome.ExitCode ?? 0),
            outcome, prepared);
    }
}
