namespace Finesse.Compatibility;

/// <summary>
/// Real read-only SetupAPI observation for M182 lifecycle Step 9.
/// A Code 43 here is PROVISIONAL pending one planned reboot, never target
/// success. Does not change any device state.
/// </summary>
public static class M195WindowsPrebootObservation
{
    public static M195LifecycleSequence.PrebootObservation Classify(
        M195LifecycleSequence.Device expectedDevice,
        M195LifecycleSequence.BoundDriver expectedBound,
        M195WindowsGpuInventory.ExactGpu observed)
    {
        ArgumentNullException.ThrowIfNull(expectedDevice);
        ArgumentNullException.ThrowIfNull(expectedBound);
        ArgumentNullException.ThrowIfNull(observed);
        if (observed.Profile != expectedDevice.Profile ||
            !string.Equals(expectedDevice.ExactHardwareId,
                observed.Profile.HardwareId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expectedDevice.InstanceId, observed.Display.InstanceId,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("M195_PREBOOT_DEVICE_PROFILE_OR_INSTANCE_CHANGED");
        string expectedInf = M195WindowsBindIdentity.RequirePublishedOemInf(
            expectedBound.PublishedInf);
        string actualInf = M195WindowsBindIdentity.RequirePublishedOemInf(
            observed.Display.PublishedInf);
        bool exactTarget = string.Equals(expectedInf, actualInf,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(expectedBound.TargetDriverVersion,
                observed.Display.Version, StringComparison.Ordinal) &&
            string.Equals(expectedBound.TargetDriverVersion,
                OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion,
                StringComparison.Ordinal);
        uint code = observed.Display.ProblemCode;
        var classification = exactTarget && code == 0
            ? M195LifecycleSequence.PrebootState.Code0AwaitingPostboot
            : exactTarget && code == 43
                ? M195LifecycleSequence.PrebootState.Code43AwaitingPostboot
                : M195LifecycleSequence.PrebootState.Reject;
        return new M195LifecycleSequence.PrebootObservation(
            observed.Display.InstanceId, actualInf, observed.Display.Version,
            checked((int)code), classification);
    }

    public static async Task<M195LifecycleSequence.PrebootObservation> ObserveAsync(
        M195LifecycleSequence.Device expectedDevice,
        M195LifecycleSequence.BoundDriver expectedBound,
        CancellationToken token = default)
    {
        var devices = await M195WindowsGpuInventory.EnumerateAsync(token);
        var selected = M195WindowsGpuInventory.RequireUniqueExactGpu(devices);
        return Classify(expectedDevice, expectedBound, selected);
    }
}
