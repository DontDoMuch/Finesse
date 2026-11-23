using System.Security.Cryptography;
using System.Text;

namespace Finesse.Compatibility;

/// <summary>
/// Exact functional Lenovo registry-policy directives from the user-provided
/// frozen M194 three-profile baseline. Only the 26/30/28 HKR policy rows are
/// retained as text; no AMD/Lenovo driver executable, original INF, DAT,
/// catalog, certificate, or proprietary binary is embedded.
/// SHA-256 pins below protect this data. Independently rebuilding 26.11.1
/// with the authentic AMD source must additionally match the frozen
/// pre-CopyINF and cleaned INF hashes.
/// </summary>
internal static class M195FrozenProfileRegistryPolicies
{
    private static readonly string[] Go1Rows = [
        "HKR,,DALNonStandardModesBCD5,%REG_BINARY%,07,20,12,80,00,00,00,00,08,00,12,80,00,00,00,00,09,00,16,00,00,00,00,00,10,00,16,00,00,00,00,00,10,80,19,20,00,00,00,00,12,00,19,20,00,00,00,00,14,40,25,60,00,00,00,00",
        "HKR,,DALRestrictedModesBCD5,%REG_BINARY%,16,00,12,00,00,00,00,00,12,80,10,24,00,00,00,00",
        "HKR,,HotkeysDisabled,%REG_DWORD%,0x1",
        "HKR,,dvr_ui_component_na,%REG_SZ%,true",
        "HKR,,DFPFreeSyncDefault,%REG_DWORD%,1",
        "HKR,,PP_WaitOnRegisterTimeout,%REG_DWORD%,0x2710",
        "HKR,,AllowWebContent,%REG_SZ%,false",
        "HKR,,LogoUrl,%REG_SZ%,hide",
        "HKR,,SystemTray,%REG_SZ%,false",
        "HKR,,DALRULE_ALLOWMONITORRANGELIMITMODESCRT,%REG_DWORD%,0",
        "HKR,,ToggleRsHotkey,%REG_SZ%,none",
        "HKR,,LCDFreeSyncDefault,%REG_DWORD%,0x7",
        "HKR,,PP_UserVariBrightLevel,%REG_DWORD%,2",
        "HKR,,Dal_UserVariBrightLevel,%REG_DWORD%,2",
        "HKR,,DalFeatureEnablePsrSU,%REG_DWORD%,0",
        "HKR,,DalDisableZ10,%REG_DWORD%,1",
        "HKR,,EnableswGCFakeCGCG,%REG_DWORD%,1",
        "HKR,,DalEmbeddedIntegerScalingSupport,%REG_DWORD%,1",
        "HKR,,DalPSRFeatureEnable,%REG_DWORD%,0",
        "HKR,,DalWirelessDisplaySupport,%REG_DWORD%,1",
        "HKR,,DalDetectRequireHpdHigh,%REG_DWORD%,0",
        "HKR,,DisableFBCSupport,%REG_DWORD%,1",
        "HKR,,BDC7EDEA37E855EFFD36,%REG_BINARY%,59,79,07,9B",
        "HKR,,BDC7EDEA40E855EFFDFB,%REG_BINARY%,59,79,07,9B",
        "HKR,,DalMinBrightnessMillinits,%REG_DWORD%,0x1388",
        "HKR,,InitDelay,%REG_DWORD%,0x7FFFFFFF",
    ];
    internal const string Go1RowsSha256 = "0C8F89B4BE1CAB61123B053DA24466635D559850B65C694BA225BFD36C22A7C7";
    private static readonly string[] GoSRows = [
        "HKR,,DALNonStandardModesBCD5,%REG_BINARY%,07,20,12,80,00,00,00,00,08,00,12,80,00,00,00,00,09,00,16,00,00,00,00,00,09,60,05,40,00,00,00,00,09,60,06,00,00,00,00,00,10,00,16,00,00,00,00,00,10,80,19,20,00,00,00,00,12,00,19,20,00,00,00,00,12,80,07,20,00,00,00,00,12,80,08,00,00,00,00,00,14,40,25,60,00,00,00,00,16,00,09,00,00,00,00,00,16,00,10,00,00,00,00,00,19,20,10,80,00,00,00,00",
        "HKR,,DalMinBrightnessMillinits,%REG_DWORD%,0x2710",
        "HKR,,DALRestrictedModesBCD5,%REG_BINARY%,08,00,06,00,00,00,00,00,10,24,07,68,00,00,00,00,12,80,10,24,00,00,00,00,16,00,12,00,00,00,00,00,16,80,10,50,00,00,00,00",
        "HKR,,DalEdgeDimmingLevelMapping,%REG_DWORD%,0x03000000",
        "HKR,,PP_WaitOnRegisterTimeout,%REG_DWORD%,0x2710",
        "HKR,,AllowWebContent,%REG_SZ%,false",
        "HKR,,LogoUrl,%REG_SZ%,hide",
        "HKR,,SystemTray,%REG_SZ%,false",
        "HKR,\"UMD\\DXVA\",DynamicContrast_ENABLE_DEF,%REG_SZ%,0",
        "HKR,,DALRULE_ALLOWMONITORRANGELIMITMODESCRT,%REG_DWORD%,0",
        "HKR,\"UMD\\DXVA\",DXVA_WMV,%REG_SZ%,0",
        "HKR,\"UMD\\DXVA\",DXVA_WMV_DEF,%REG_SZ%,0",
        "HKR,,DXVA_WMV_NA,%REG_SZ%,1",
        "HKR,,DalSupportExternalPanelDrr,%REG_DWORD%,1",
        "HKR,,DalDisableZ10,%REG_DWORD%,1",
        "HKR,,DalEdgeDimmingLevelMapping,%REG_DWORD%,0x03000000",
        "HKR,,EnableswGCForceAtcL24KbWa,%REG_DWORD%,1",
        "HKR,,DalPSRFeatureEnable,%REG_DWORD%,1",
        "HKR,,DisableFBCSupport,%REG_DWORD%,0",
        "HKR,,SmartDCSupported,%REG_DWORD%,1",
        "HKR,,SmartDCDefMode,%REG_DWORD%,0",
        "HKR,,DalDisplayableSupport,%REG_DWORD%,0",
        "HKR,,Gmx_EnableHwFlipQueue,%REG_DWORD%,0",
        "HKR,,KmdEnableHwFlipQueue,%REG_DWORD%,0",
        "HKR,,BDC72DEA40E855EFFDFB, %REG_BINARY%,59,79,07,9B",
        "HKR,,BDC7EDEA37E855EFFD36, %REG_BINARY%,59,79,07,9B",
        "HKR,,BDC7EDEA40E855EFFDFB, %REG_BINARY%,59,79,07,9B",
        "HKR,,BDC7F3EA40E855EFFDFB, %REG_BINARY%,59,79,07,9B",
        "HKR,,PP_UserVariBrightLevel,%REG_DWORD%,2",
        "HKR,,Dal_UserVariBrightLevel,%REG_DWORD%,2",
    ];
    internal const string GoSRowsSha256 = "E3AB1232BD66F0DD1FA4E862307ABC544295F8BA7F37B4C6E936A2585996BD56";
    private static readonly string[] Go2Rows = [
        "HKR,,DALNonStandardModesBCD5,%REG_BINARY%,07,20,12,80,00,00,00,00,08,00,12,80,00,00,00,00,09,00,16,00,00,00,00,00,10,00,16,00,00,00,00,00,10,80,19,20,00,00,00,00,12,00,19,20,00,00,00,00,14,40,25,60,00,00,00,00",
        "HKR,,ProblemReportUrl,%REG_SZ%,show",
        "HKR,,DALRestrictedModesBCD5,%REG_BINARY%,16,00,12,00,00,00,00,00,12,80,10,24,00,00,00,00",
        "HKR,,HotkeysDisabled,%REG_DWORD%,0x1",
        "HKR,,dvr_ui_component_na,%REG_SZ%,true",
        "HKR,,DFPFreeSyncDefault,%REG_DWORD%,1",
        "HKR,,PP_WaitOnRegisterTimeout,%REG_DWORD%,0x2710",
        "HKR,,mobile_runtime_component_NA,%REG_SZ%,true",
        "HKR,,AllowWebContent,%REG_SZ%,false",
        "HKR,,LogoUrl,%REG_SZ%,hide",
        "HKR,,SystemTray,%REG_SZ%,false",
        "HKR,,DalWFDEnable,%REG_DWORD%,1",
        "HKR,\"UMD\\DXVA\",ColorVibrance_DEF,%REG_SZ%,40",
        "HKR,\"UMD\\DXVA\",ColorVibrance_ENABLE_DEF,%REG_SZ%,0",
        "HKR,,DALRULE_ALLOWMONITORRANGELIMITMODESCRT,%REG_DWORD%,0",
        "HKR,,InitDelay,%REG_DWORD%,0x7FFFFFFF",
        "HKR,,ToggleRsHotkey,%REG_SZ%,none",
        "HKR,,LCDFreeSyncDefault,%REG_DWORD%,0x7",
        "HKR,,ShowRSOverlay,%REG_SZ%,false",
        "HKR,,DalMinBrightnessMillinits,%REG_DWORD%,0x1388",
        "HKR,,DalFeatureEnablePsrSU,%REG_DWORD%,0",
        "HKR,,DalDisableZ10,%REG_DWORD%,1",
        "HKR,,EnableswGCForceAtcL24KbWa,%REG_DWORD%,1",
        "HKR,,DalWirelessDisplaySupport,%REG_DWORD%,1",
        "HKR,,DalDetectRequireHpdHigh,%REG_DWORD%,0",
        "HKR,,BDC7E3EAF1E855EFFD3A, %REG_BINARY%,9B,16,09,9B",
        "HKR,,PP_UserVariBrightLevel,%REG_DWORD%,3",
        "HKR,,Dal_UserVariBrightLevel,%REG_DWORD%,3",
    ];
    internal const string Go2RowsSha256 = "420993CA99FD79B08C9440665F50831E757D97274C490D29C9A5C902ADBF728E";

    internal static string RequireAuthorized(HandheldProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        (string[] lines, string sha) = profile.Id switch
        {
            "LegionGo1" => (Go1Rows, Go1RowsSha256),
            "LegionGoS-Z1Extreme" => (GoSRows, GoSRowsSha256),
            "LegionGo2-Z2Extreme" => (Go2Rows, Go2RowsSha256),
            _ => throw new InvalidDataException("M195_NO_AUTHORIZED_PROFILE_POLICY")
        };
        if (profile != HandheldProfiles.Resolve(profile.HardwareId) ||
            lines.Length != profile.ExpectedLenovoPolicyCount ||
            lines.Any(row => !row.StartsWith("HKR,", StringComparison.Ordinal)))
            throw new InvalidDataException("M195_FROZEN_POLICY_PROFILE_COUNT_INVALID");
        string policy = string.Join("\n", lines);
        string actual = Convert.ToHexString(SHA256.HashData(
            Encoding.Latin1.GetBytes(policy)));
        if (!string.Equals(actual, sha, StringComparison.Ordinal))
            throw new InvalidDataException("M195_FROZEN_POLICY_SOURCE_SHA_MISMATCH");
        return policy;
    }
}
