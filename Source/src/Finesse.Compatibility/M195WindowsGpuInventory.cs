using System.Runtime.InteropServices;
using System.Text;

namespace Finesse.Compatibility;

/// <summary>
/// Read-only SetupAPI/Configuration Manager display adapter, ported from the
/// exact frozen M194 WindowsSignedDriverSource + M147 device selection.
/// No WMI locale dependencies, PnP mutation, driver binding, or registry writes.
/// </summary>
public static class M195WindowsGpuInventory
{
    public static readonly Guid DisplayClass =
        new("4D36E968-E325-11CE-BFC1-08002BE10318");
    private const uint DigcfPresent = 2;
    private const int ErrorNoMoreItems = 259;

    public sealed record ObservedDisplay(
        string InstanceId, IReadOnlyList<string> HardwareIds, Guid ClassGuid,
        string PublishedInf, string Provider, string Version, string Service,
        uint DevNodeStatus, uint ProblemCode);

    public sealed record ExactGpu(ObservedDisplay Display, HandheldProfile Profile);

    /// <summary>
    /// Pure, independently testable device selection. Reject multiple matches,
    /// wrong revisions, wrong subsystems, non-display devices and ambiguous GPUs.
    /// </summary>
    public static ExactGpu RequireUniqueExactGpu(
        IReadOnlyList<ObservedDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var matches = new List<ExactGpu>();
        foreach (ObservedDisplay device in displays)
        {
            if (device.ClassGuid != DisplayClass)
                continue;
            // Frozen M194 Go1 permitted the revisionless HardwareIds form.
            // Go S and Go 2 must retain exact REV/subsystem matching. Reject
            // any contradictory explicit Go1 revision instead of masking it
            // with the fallback ID on the same devnode.
            string go1Base = HandheldProfiles.Go1.HardwareId[
                ..HandheldProfiles.Go1.HardwareId.IndexOf("&REV_", StringComparison.Ordinal)];
            if (device.HardwareIds.Any(id =>
                id.StartsWith(go1Base + "&REV_", StringComparison.OrdinalIgnoreCase) &&
                !id.Equals(HandheldProfiles.Go1.HardwareId, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("M195_GO1_CONFLICTING_EXPLICIT_REVISION");

            HandheldProfile[] profileHits = device.HardwareIds
                .Select(id => HandheldProfiles.Resolve(id) ??
                    (string.Equals(id, go1Base, StringComparison.OrdinalIgnoreCase)
                        ? HandheldProfiles.Go1 : null))
                .Where(x => x is not null).Cast<HandheldProfile>()
                .Distinct().ToArray();
            if (profileHits.Length > 1)
                throw new InvalidDataException("M195_MULTIPLE_COMPETING_HARDWARE_PROFILES");
            if (profileHits.Length == 1)
                matches.Add(new ExactGpu(device, profileHits[0]));
        }
        if (matches.Count != 1)
            throw new InvalidDataException("M195_EXACT_DISPLAY_GPU_MATCH_COUNT_" + matches.Count);
        if (string.IsNullOrWhiteSpace(matches[0].Display.InstanceId))
            throw new InvalidDataException("M195_GPU_INSTANCE_ID_MISSING");
        return matches[0];
    }

    /// <summary>Read-only native Windows enumeration of present display-class devices.</summary>
    public static Task<IReadOnlyList<ObservedDisplay>> EnumerateAsync(
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native SetupAPI requires Windows.");
        return Task.FromResult(EnumerateNative(token));
    }

    public static bool EligibleForRecoveryExport(ObservedDisplay device)
    {
        ArgumentNullException.ThrowIfNull(device);
        return device.ClassGuid == DisplayClass &&
            device.ProblemCode == 0 &&
            (device.DevNodeStatus & 0x00000008u) != 0 &&
            System.Text.RegularExpressions.Regex.IsMatch(
                device.PublishedInf ?? string.Empty, @"^oem[0-9]+\.inf$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant) &&
            !device.Service.Equals("BasicDisplay", StringComparison.OrdinalIgnoreCase) &&
            !device.Provider.Equals("Microsoft", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ObservedDisplay> EnumerateNative(CancellationToken token)
    {
        Guid display = DisplayClass;
        IntPtr handle = SetupDiGetClassDevsW(ref display, null, IntPtr.Zero, DigcfPresent);
        if (handle == new IntPtr(-1))
            throw new InvalidOperationException("M195_SETUPDI_GET_CLASS_DEVS_FAILED_" +
                Marshal.GetLastWin32Error());
        try
        {
            var result = new List<ObservedDisplay>();
            for (uint index = 0; ; index++)
            {
                token.ThrowIfCancellationRequested();
                var info = new SpDevInfoData
                {
                    cbSize = (uint)Marshal.SizeOf<SpDevInfoData>()
                };
                if (!SetupDiEnumDeviceInfo(handle, index, ref info))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == ErrorNoMoreItems)
                        break;
                    throw new InvalidOperationException(
                        "M195_SETUPDI_ENUM_DEVICE_FAILED_" + error);
                }
                IReadOnlyList<string> ids = GetMultiString(handle, ref info, KeyHardwareIds);
                if (ids.Count == 0)
                    continue;
                uint status = 0, problem = 0;
                uint cmStatus = CmGetDevNodeStatus(out status, out problem, info.DevInst, 0);
                if (cmStatus != 0)
                    throw new InvalidOperationException("M195_CFGMGR_DEVNODE_FAILED_" + cmStatus);
                result.Add(new ObservedDisplay(
                    GetString(handle, ref info, KeyInstanceId),
                    ids, info.ClassGuid,
                    GetString(handle, ref info, KeyInfPath),
                    GetString(handle, ref info, KeyProvider),
                    GetString(handle, ref info, KeyVersion),
                    GetString(handle, ref info, KeyService),
                    status, problem));
            }
            return result.AsReadOnly();
        }
        finally
        {
            if (!SetupDiDestroyDeviceInfoList(handle))
                throw new InvalidOperationException("M195_SETUPDI_DESTROY_LIST_FAILED");
        }
    }

    private static byte[] GetRaw(IntPtr set, ref SpDevInfoData info, DevPropKey key)
    {
        uint propertyType, needed = 0;
        SetupDiGetDevicePropertyW(set, ref info, ref key, out propertyType,
            null, 0, out needed, 0);
        if (needed == 0)
            return [];
        if (needed > 131072)
            throw new InvalidDataException("M195_DEVICE_PROPERTY_TOO_LARGE");
        var buffer = new byte[needed];
        if (!SetupDiGetDevicePropertyW(set, ref info, ref key, out propertyType,
                buffer, (uint)buffer.Length, out needed, 0))
            throw new InvalidOperationException("M195_DEVICE_PROPERTY_READ_FAILED_" +
                Marshal.GetLastWin32Error());
        return buffer;
    }

    private static string GetString(IntPtr set, ref SpDevInfoData info, DevPropKey key)
    {
        byte[] raw = GetRaw(set, ref info, key);
        return raw.Length == 0 ? string.Empty :
            Encoding.Unicode.GetString(raw).TrimEnd('\0');
    }

    private static IReadOnlyList<string> GetMultiString(
        IntPtr set, ref SpDevInfoData info, DevPropKey key) =>
        GetString(set, ref info, key).Split('\0',
            StringSplitOptions.RemoveEmptyEntries);

    [StructLayout(LayoutKind.Sequential)]
    private struct DevPropKey
    {
        public Guid fmtid;
        public uint pid;
        public DevPropKey(Guid format, uint propertyId) { fmtid = format; pid = propertyId; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    private static readonly DevPropKey KeyInstanceId =
        new(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);
    private static readonly DevPropKey KeyHardwareIds =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 3);
    private static readonly DevPropKey KeyInfPath =
        new(new Guid("A8B865DD-2E3D-4094-AD97-E593A70C75D6"), 5);
    private static readonly DevPropKey KeyProvider =
        new(new Guid("A8B865DD-2E3D-4094-AD97-E593A70C75D6"), 9);
    private static readonly DevPropKey KeyVersion =
        new(new Guid("A8B865DD-2E3D-4094-AD97-E593A70C75D6"), 3);
    private static readonly DevPropKey KeyService =
        new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 6);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(
        ref Guid classGuid, string? enumerator, IntPtr owner, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr set, uint index, ref SpDevInfoData info);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDevicePropertyW(
        IntPtr set, ref SpDevInfoData info, ref DevPropKey key,
        out uint propertyType, [Out] byte[]? buffer, uint bufferBytes,
        out uint required, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_Status")]
    private static extern uint CmGetDevNodeStatus(
        out uint status, out uint problem, uint devInst, uint flags);
}
