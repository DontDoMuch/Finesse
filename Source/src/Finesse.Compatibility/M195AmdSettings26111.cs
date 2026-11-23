using System.Text.RegularExpressions;

namespace Finesse.Compatibility;

/// <summary>
/// Frozen M169/M184 AMD 26.11.1 desktop Settings MSI identity and pure
/// Windows Installer fallback decision rules. These pins MUST be replaced
/// for a newer AMD release, never reused by an automatic updater.
/// </summary>
public static class M195AmdSettings26111
{
    public const string TargetDriverVersion = "32.0.32015.16001";
    public const string SourceCccRelative = "B026778/ccc2_install.exe";
    public const long CccBytes = 246_780_968;
    public const string CccSha256 =
        "4E3437C2DFF51B6B72A814A010F77679B273A8C355AB849BAD836206FB9B3915";
    public const string MsiRelative = "CN/cnext/cnext64/ccc-next64.msi";
    public const long MsiBytes = 155_561_984;
    public const string MsiSha256 =
        "B1AFF2C1494877E15E23025D6B09F898E8E422777DA00CA013695181D7F116F2";
    public const string ProductCode = "{9E860308-0949-4D1D-BABD-980EEC7CE8CD}";
    public const string UpgradeCode = "{A837E2B7-5BA6-4286-BC21-A002FCDD9A85}";
    public const string ProductName = "AMD Settings";
    public const string PublisherContains = "Advanced Micro Devices";
    public const string ProductVersion = "2026.1109.2249.2112";
    public const string Portable7zipReleaseSha256 =
        "9B697F65ECC2D9B0152F5EE96D72DDE522A7827690349D9307935DACF40F42EC";
    public const string Portable7zipReleaseFileName = "7z2606-x64.exe";
    public const string NativeFrontendRelative = "AMD/CNext/CNext/RadeonSoftware.exe";

    private static readonly Regex LaunchConditionsFailed = new(
        @"(?m)^\s*Action ended[^\r\n]*:\s*LaunchConditions\.\s*Return value 3\.",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex LogDowngrade = new(
        @"(?m)^\s*Property\(S\):\s*WIX_DOWNGRADE_DETECTED\s*=\s*(?<codes>[^\r\n]+)\s*$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>
    /// A 1603 alone is NEVER proof of an AMD Settings downgrade. Only the
    /// target's own fresh verbose log with FindRelatedProducts, failed
    /// LaunchConditions and ONE parseable WIX_DOWNGRADE_DETECTED property can
    /// authorize subsequent native registered-product inspection.
    /// </summary>
    public static IReadOnlyList<string> ReadVerifiedDowngradeBlockerCodes(
        int targetMsiExitCode, string? exactAttemptLog)
    {
        if (targetMsiExitCode != 1603 || string.IsNullOrWhiteSpace(exactAttemptLog) ||
            exactAttemptLog.Length > 8_000_000 ||
            !exactAttemptLog.Contains("FindRelatedProducts", StringComparison.Ordinal) ||
            !LaunchConditionsFailed.IsMatch(exactAttemptLog))
            return Array.Empty<string>();
        MatchCollection matches = LogDowngrade.Matches(exactAttemptLog);
        if (matches.Count != 1)
            return Array.Empty<string>();
        string[] entries = matches[0].Groups["codes"].Value.Trim().Split(
            ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length is < 1 or > 10)
            return Array.Empty<string>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in entries)
        {
            if (!Guid.TryParseExact(entry, "B", out Guid value) ||
                !unique.Add(value.ToString("B").ToUpperInvariant()))
                return Array.Empty<string>();
        }
        return unique.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public sealed record RegisteredProduct(string Code, string Name,
        string Publisher, string Version, bool Installed, bool RelatedByExactUpgradeCode);

    /// <summary>
    /// Only an exact MSI-authored blocker which ALSO appears in the native
    /// MsiEnumRelatedProducts (same UpgradeCode), ProductName and publisher
    /// evidence is eligible. Unknown GUIDs, duplicates, target's ProductCode,
    /// foreign registrations and ambiguous matches all cause refusal.
    /// </summary>
    public static IReadOnlyList<RegisteredProduct> RequireVerifiedBlockers(
        IReadOnlyList<string> codes, IReadOnlyList<RegisteredProduct> relatedProducts)
    {
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(relatedProducts);
        if (codes.Count is < 1 or > 10 ||
            relatedProducts.Select(x => x.Code).Distinct(
                StringComparer.OrdinalIgnoreCase).Count() != relatedProducts.Count)
            throw new InvalidDataException("M195_M184_AMBIGUOUS_OR_EMPTY_BLOCKER_INVENTORY");
        var output = new List<RegisteredProduct>();
        foreach (string code in codes)
        {
            if (!Guid.TryParseExact(code, "B", out Guid value))
                throw new InvalidDataException("M195_M184_LOG_BLOCKER_GUID_INVALID");
            string normalized = value.ToString("B").ToUpperInvariant();
            if (normalized.Equals(ProductCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("M195_M184_CANNOT_REMOVE_TARGET_MSI");
            var matches = relatedProducts.Where(p =>
                string.Equals(p.Code, normalized, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || !matches[0].Installed ||
                !matches[0].RelatedByExactUpgradeCode ||
                !string.Equals(matches[0].Name, ProductName, StringComparison.Ordinal) ||
                !matches[0].Publisher.Contains(PublisherContains,
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(matches[0].Version))
                throw new InvalidDataException("M195_M184_LOG_BLOCKER_NOT_VERIFIED_AMD_SETTINGS");
            output.Add(matches[0]);
        }
        if (output.Select(p => p.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            codes.Count)
            throw new InvalidDataException("M195_M184_DUPLICATE_BLOCKER_IN_LOG");
        return output.AsReadOnly();
    }

    /// <summary>
    /// Centrally owned silent MSI command contract. /qn prevents the
    /// previously observed visible AMD Settings downgrade prompt, while
    /// /norestart defers the one planned reboot until AFTER Step 10.
    /// </summary>
    public static IReadOnlyList<string> BuildMsiexecArguments(
        bool install, string target, string newVerboseLog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(newVerboseLog);
        if (!newVerboseLog.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            (install && !target.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("M195_MSI_INVALID_TARGET_OR_LOG");
        if (!install)
        {
            if (!Guid.TryParseExact(target, "B", out Guid product) ||
                string.Equals(product.ToString("B"), ProductCode,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("M195_MSI_UNINSTALL_MUST_BE_RELATED_BLOCKER_NOT_TARGET");
        }
        return [install ? "/i" : "/x", target,
            "/qn", "/norestart", "/L*V", newVerboseLog];
    }

    public static bool IsTargetMsiSuccess(int exitCode) => exitCode is 0 or 3010;
}
