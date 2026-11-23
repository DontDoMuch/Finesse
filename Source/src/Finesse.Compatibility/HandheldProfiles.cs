namespace Finesse.Compatibility;

/// <summary>
/// Exact hardware identification contract from frozen M194. No prefix or
/// partial-model fallback: revisions, subsystems and device IDs are mandatory.
/// </summary>
public sealed record HandheldProfile(
    string Id,
    string HardwareId,
    ushort DeviceId,
    byte Revision,
    string Family,
    string InstallSection,
    int ExpectedLenovoPolicyCount);

public static class HandheldProfiles
{
    public static readonly HandheldProfile Go1 = new(
        "LegionGo1-Z1Extreme",
        @"PCI\VEN_1002&DEV_15BF&SUBSYS_381217AA&REV_04",
        0x15BF, 0x04,
        "Phoenix",
        "ati2mtag_Phoenix_LegionGo",
        26);

    public static readonly HandheldProfile GoS = new(
        "LegionGoS-Z1Extreme",
        @"PCI\VEN_1002&DEV_15BF&SUBSYS_380C17AA&REV_04",
        0x15BF, 0x04,
        "Phoenix",
        "ati2mtag_Phoenix_LegionGoS",
        30);

    public static readonly HandheldProfile Go2 = new(
        "LegionGo2-Z2Extreme",
        @"PCI\VEN_1002&DEV_150E&SUBSYS_381C17AA&REV_C5",
        0x150E, 0xC5,
        "Strix",
        "ati2mtag_Strix_LegionGo2",
        28);

    private static readonly HandheldProfile[] All = [Go1, GoS, Go2];

    /// <summary>
    /// Full hardware-ID match only. An unsupported revision or subsystem
    /// is not silently routed to another Lenovo handheld profile.
    /// </summary>
    public static HandheldProfile? Resolve(string? hardwareId)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
            return null;
        foreach (HandheldProfile profile in All)
        {
            if (string.Equals(profile.HardwareId, hardwareId.Trim(),
                              StringComparison.OrdinalIgnoreCase))
                return profile;
        }
        return null;
    }
}
