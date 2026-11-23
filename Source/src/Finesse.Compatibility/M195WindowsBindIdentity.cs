using System.Text.RegularExpressions;

namespace Finesse.Compatibility;

/// <summary>
/// Public, READ-ONLY port of M179's device-bind identity checks. Selecting
/// the exact PnP HardwareId and verifying target INF mapping are prerequisites
/// to SetupCopyOEMInf / UpdateDriverForPlugAndPlayDevices. This class never
/// invokes either native mutating API.
/// </summary>
public static class M195WindowsBindIdentity
{
    private static readonly Regex PublishedOemInf = new(
        @"\Aoem[0-9]+\.inf\z", RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    public sealed record ProposedBind(string InstanceId, string HardwareId,
        string DriverInstallSection, HandheldProfile Profile);

    public static ProposedBind InspectProposedBind(
        M195WindowsGpuInventory.ExactGpu observed, string candidateInf)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(candidateInf);
        if (string.IsNullOrWhiteSpace(candidateInf) || candidateInf.Length > 5_000_000)
            throw new InvalidDataException("M195_BIND_INVALID_INF_TEXT");
        if (observed.Display.ClassGuid != M195WindowsGpuInventory.DisplayClass ||
            observed.Profile != HandheldProfiles.Resolve(observed.Profile.HardwareId) ||
            string.IsNullOrWhiteSpace(observed.Display.InstanceId))
            throw new InvalidDataException("M195_BIND_UNVERIFIED_GPU_PROFILE");

        string expected = observed.Profile.HardwareId;
        string installSection = observed.Profile.InstallSection;
        // Confirm exactly one M194-style manufacturer device mapping for the
        // selected profile. Do not search for a partial PCI substring.
        string pattern = @"(?m)^[ \t]*""[^""\r\n]+""[ \t]*=[ \t]*" +
            Regex.Escape(installSection) + @"[ \t]*,[ \t]*" +
            Regex.Escape(expected) + @"[ \t]*(?:;[^\r\n]*)?$";
        MatchCollection lines = Regex.Matches(candidateInf, pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
        if (lines.Count != 1)
            throw new InvalidDataException("M195_BIND_TARGET_INF_MAPPING_NOT_UNIQUE");

        // Frozen M179 chooses a real HardwareIds member whose exact bytes
        // appear in the new INF, preferring the longest matching identity.
        // No fabricated revisionless binding name is permitted.
        var applicable = observed.Display.HardwareIds
            .Where(id => !string.IsNullOrWhiteSpace(id) &&
                (id.Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                 (observed.Profile == HandheldProfiles.Go1 &&
                  id.Equals(expected[..expected.IndexOf("&REV_", StringComparison.Ordinal)],
                      StringComparison.OrdinalIgnoreCase))))
            .Where(id => Regex.IsMatch(candidateInf,
                @"(?<![A-Za-z0-9_&])" + Regex.Escape(id) +
                @"(?![A-Za-z0-9_&])", RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(id => id.Length)
            .ToArray();
        if (applicable.Length == 0)
            throw new InvalidDataException("M195_BIND_NO_EXACT_HARDWARE_ID_IN_INF");
        return new ProposedBind(observed.Display.InstanceId,
            applicable[0], installSection, observed.Profile);
    }

    public static string RequirePublishedOemInf(string publishedInf)
    {
        if (string.IsNullOrWhiteSpace(publishedInf) ||
            Path.GetFileName(publishedInf) != publishedInf ||
            !PublishedOemInf.IsMatch(publishedInf))
            throw new InvalidDataException("M195_BIND_INVALID_PUBLISHED_INF");
        return publishedInf.ToLowerInvariant();
    }
}
