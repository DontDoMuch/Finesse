using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Finesse.Compatibility;

/// <summary>
/// Copies installation sections from a caller-supplied official AMD INF.
/// Contains only transformation logic, not proprietary AMD or Lenovo INF bytes.
/// This class alone does NOT sign catalogs or install drivers.
/// </summary>
public static class AmdInfSourceRebuilder
{
    private const string ManufacturerSection = "[ATI.Mfg.NTamd64.10.0.1..19044]\r\n";
    private const string Annotation = "\r\n; Finesse M159 private offline 26.11.1 device profile - no install authorization\r\n\r\n";
    private static readonly string[] ClonedSuffixes =
        ["", ".Services", ".HW", ".Software", ".Components", ".GeneralConfigData"];
    private const string OriginalOfficial26111InfSha =
        "2E1125EB79359D3BAEB283B81868064257C5F1436D8928AC2EC123837DD70C57";
    private const string CopyCp = "CopyINF = .\\amdcp\\amdcp.inf\r\n";
    private const string CopyUw = "CopyINF=amduw23e.inf\r\n";

    public sealed record InfResult(
        string OriginalSha256, string ProfileInfSha256, string CleanedInfSha256,
        byte[] CleanedInf);

    /// <summary>
    /// Rebuilds only the exact frozen M194 26.11.1 candidate from externally
    /// supplied AMD source and profile-specific registry directives.
    /// Registry directives must come from a separately authorized source; do
    /// not redistribute private Lenovo/AMD INF or a modified INF with this code.
    /// </summary>
    public static InfResult RebuildPinned26111(
        byte[] officialInf, HandheldProfile profile, string registryPolicy)
    {
        ArgumentNullException.ThrowIfNull(officialInf);
        ArgumentNullException.ThrowIfNull(profile);
        string originalHash = Sha256(officialInf);
        if (!string.Equals(originalHash, OriginalOfficial26111InfSha, StringComparison.Ordinal))
            throw new InvalidDataException("AMD_26111_OFFICIAL_INF_SOURCE_HASH_MISMATCH");

        string preHash = profile.Id switch
        {
            "LegionGo1" => "A8C953394300180E33AC5D96611CCC0EBFBCC16C6D18618598ACBC237C1CDE65",
            "LegionGoS-Z1Extreme" => "FBF807952823DF6F87529841E3804C5AA5EA9B6C10E3E1EB303E9EA52E0539D8",
            "LegionGo2-Z2Extreme" => "5DF3130B11CB52F87783CCA7B0895DEBA8871DFCDE9550A2F246B6EA14D266E4",
            _ => throw new InvalidDataException("AMD_INF_UNSUPPORTED_M194_PROFILE")
        };
        string cleanedHash = profile.Id switch
        {
            "LegionGo1" => "5F49F240CBDA41F7BD6E30867751507ACF38386B7AA3E0848639BCD002FE691A",
            "LegionGoS-Z1Extreme" => "E2B0809F3E3D135BFD03F1E0D97D1140BD6A05557024CE1D3EB604DFDAA90071",
            "LegionGo2-Z2Extreme" => "A355187F07FEEE5A0E0EDCC772A95C68E4F54B04323DE3D45634FFBE447CCBA3",
            _ => throw new InvalidDataException("AMD_INF_UNSUPPORTED_M194_PROFILE")
        };

        byte[] prepared = BuildProfileInf(officialInf, profile, registryPolicy);
        if (Sha256(prepared) != preHash)
            throw new InvalidDataException("AMD_26111_PROFILE_INF_FROZEN_SHA_MISMATCH");

        string preparedText = Encoding.Latin1.GetString(prepared);
        string closed = AmdInfCopyInfClosure.RemoveVerifiedDependencies(preparedText, profile,
            [new VerifiedCopyInfRemoval(CopyCp, 11),
             new VerifiedCopyInfRemoval(CopyUw, 11)]);
        byte[] result = Encoding.Latin1.GetBytes(closed);
        if (Sha256(result) != cleanedHash)
            throw new InvalidDataException("AMD_26111_CLEANED_INF_FROZEN_SHA_MISMATCH");
        return new InfResult(originalHash, preHash, cleanedHash, result);
    }

    /// <summary>
    /// Constructs an UNSIGNED profile INF from a caller's AMD INF and custom
    /// registry policy lines. This low-level transformer does not authenticate
    /// a release. For a 26.11.1 installation candidate, call RebuildPinned26111.
    /// </summary>
    public static byte[] BuildProfileInf(
        byte[] originalInf, HandheldProfile profile, string registryPolicy)
    {
        ArgumentNullException.ThrowIfNull(originalInf);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(registryPolicy);
        HandheldProfile? known = HandheldProfiles.Resolve(profile.HardwareId);
        if (known is null || known != profile)
            throw new InvalidDataException("AMD_INF_UNSUPPORTED_OR_MUTATED_PROFILE");
        string source = Encoding.Latin1.GetString(originalInf);
        if (source.Contains('\n') && source.Replace("\r\n", "", StringComparison.Ordinal).Contains('\n'))
            throw new InvalidDataException("AMD_INF_NON_CRLF_SOURCE_UNSUPPORTED");
        if (Count(source, ManufacturerSection) != 1)
            throw new InvalidDataException("AMD_INF_MFG_ANCHOR_MISSING_OR_DUPLICATED");
        if (source.Contains(profile.HardwareId, StringComparison.OrdinalIgnoreCase) ||
            source.Contains("LegionGo_Finesse_", StringComparison.Ordinal))
            throw new InvalidDataException("AMD_INF_ALREADY_PATCHED_OR_AMBIGUOUS");

        string family = profile.Family switch
        {
            "Phoenix" => "Phoenix",
            "Strix" => "Strix",
            _ => throw new InvalidDataException("AMD_INF_UNKNOWN_FAMILY")
        };
        string suffix = profile.Id switch
        {
            "LegionGo1" => "Go1",
            "LegionGoS-Z1Extreme" => "GoS",
            "LegionGo2-Z2Extreme" => "Go2",
            _ => throw new InvalidDataException("AMD_INF_UNSUPPORTED_PROFILE_ID")
        };
        string policyName = "LegionGo_Finesse_" + suffix + "_Policy";
        string[] registryLines = ValidateRegistryPolicy(registryPolicy, profile.ExpectedLenovoPolicyCount);
        string label = profile.DeviceId switch
        {
            0x15BF => "AMD15BF.1",
            0x150E => "AMD150E.1",
            _ => throw new InvalidDataException("AMD_INF_UNKNOWN_DEVICE")
        };

        var builder = new StringBuilder(source.Length + 8_192);
        string mapping = "\"%" + label + "%\" = " + profile.InstallSection +
            ", " + profile.HardwareId + "\r\n";
        int insert = source.IndexOf(ManufacturerSection, StringComparison.Ordinal) +
            ManufacturerSection.Length;
        builder.Append(source.AsSpan(0, insert));
        builder.Append(mapping);
        builder.Append(source.AsSpan(insert));
        builder.Append(Annotation);

        foreach (string sectionSuffix in ClonedSuffixes)
        {
            string body = ExtractSectionBody(source, "ati2mtag_" + family + sectionSuffix);
            builder.Append('[').Append(profile.InstallSection).Append(sectionSuffix)
                .Append("]\r\n");
            builder.Append(body);
            if (sectionSuffix.Length == 0)
                builder.Append("AddReg = ").Append(policyName).Append("\r\n");
            builder.Append("\r\n");
        }
        builder.Append('[').Append(policyName).Append("]\r\n");
        foreach (string row in registryLines)
            builder.Append(row).Append("\r\n");
        return Encoding.Latin1.GetBytes(builder.ToString());
    }

    private static string[] ValidateRegistryPolicy(string policy, int expectedCount)
    {
        string normalized = policy.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Contains('\r'))
            throw new InvalidDataException("AMD_INF_POLICY_MIXED_NEWLINES");
        string[] rows = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length != expectedCount)
            throw new InvalidDataException("AMD_INF_LENOVO_POLICY_COUNT_MISMATCH");
        foreach (string row in rows)
        {
            if (!row.StartsWith("HKR,", StringComparison.OrdinalIgnoreCase) ||
                row.Contains('[') || row.Contains(']') || row.Contains('\0'))
                throw new InvalidDataException("AMD_INF_POLICY_ROW_INVALID");
        }
        return rows;
    }

    private static string ExtractSectionBody(string source, string section)
    {
        string header = "[" + section + "]\r\n";
        if (Count(source, header) != 1)
            throw new InvalidDataException("AMD_INF_MISSING_OR_DUPLICATED_FAMILY_SECTION: " + section);
        int start = source.IndexOf(header, StringComparison.Ordinal) + header.Length;
        var headers = new Regex(@"^\[[^\]\r\n]+\]\r\n", RegexOptions.Multiline,
            TimeSpan.FromSeconds(2));
        Match next = headers.Match(source, start);
        int end = next.Success ? next.Index : source.Length;
        string body = source[start..end].TrimEnd('\r', '\n');
        return body.Length == 0 ? "" : body + "\r\n";
    }

    private static int Count(string source, string needle) =>
        source.Split([needle], StringSplitOptions.None).Length - 1;

    public static string Sha256(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));
}
