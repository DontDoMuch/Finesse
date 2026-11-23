using System.Security.Cryptography;
using System.Text;
using Finesse.Compatibility;

// Offline, file-only compatibility-recipe reproduction.
// No privileged operations; does not sign, stage or install a GPU driver.
const string release = "26.20.15.16-261109a-206030E-AMD-Software-Adrenalin-Edition";
const string originalDatHash = "2DC60D0B41488E504E78213B8DA1DF790E7E39AF67A892E9441F41642659E459";
try
{
    if (args.Length != 10)
        throw new ArgumentException(
            "Usage: --original-inf PATH --original-dat PATH --lenovo-policy PATH --profile Go1|GoS|Go2 --output-dir NEW_DIRECTORY");

    var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
            !parsed.TryAdd(args[i], args[i + 1]) ||
            string.IsNullOrWhiteSpace(args[i + 1]))
            throw new ArgumentException("Invalid/duplicate option: " + args[i]);
    }
    if (parsed.Count != 5)
        throw new ArgumentException("Incorrect number of unique options.");
    string Required(string option) => parsed.TryGetValue(option, out string? value)
        ? value : throw new ArgumentException("Missing option: " + option);

    HandheldProfile profile = Required("--profile") switch
    {
        "Go1" => HandheldProfiles.Go1,
        "GoS" => HandheldProfiles.GoS,
        "Go2" => HandheldProfiles.Go2,
        _ => throw new ArgumentException("Unsupported exact handheld profile")
    };
    string infPath = Path.GetFullPath(Required("--original-inf"));
    string datPath = Path.GetFullPath(Required("--original-dat"));
    string policyPath = Path.GetFullPath(Required("--lenovo-policy"));
    string outputRoot = Path.GetFullPath(Required("--output-dir"));

    if (!File.Exists(infPath) || !File.Exists(datPath) || !File.Exists(policyPath))
        throw new FileNotFoundException("All three source files must exist.");
    if (Directory.Exists(outputRoot) || File.Exists(outputRoot))
        throw new IOException("Output path already exists. Refusing overwrite.");
    if (Directory.GetParent(outputRoot) is not { } parent)
        throw new ArgumentException("Output must have a parent directory.");
    string[] files = [infPath, datPath, policyPath];
    if (files.Any(p => p.StartsWith(outputRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("Source files cannot be inside output folder.");

    byte[] sourceInf = File.ReadAllBytes(infPath);
    byte[] sourceDat = File.ReadAllBytes(datPath);
    string policy = File.ReadAllText(policyPath, Encoding.Latin1);
    if (AmdInfSourceRebuilder.Sha256(sourceDat) != originalDatHash)
        throw new InvalidDataException("Official AMD 26.11.1 original DAT SHA-256 mismatch.");

    // Pinned rebuild checks the authentic original INF SHA, profile-specific
    // frozen INF SHA, 11+11 CopyINF removals and final frozen INF SHA.
    AmdInfSourceRebuilder.InfResult inf = AmdInfSourceRebuilder.RebuildPinned26111(
        sourceInf, profile, policy);

    // The DAT builder validates the original release-specific MetroHash and
    // round-trip-reconstructs every original record, rather than using a blob.
    byte[] dat = AmdDatReleaseRebuilder.BuildForVerifiedProfile(
        sourceDat, release, profile);
    string datHash = AmdInfSourceRebuilder.Sha256(dat);
    string expectedDat = profile.Id == HandheldProfiles.Go2.Id
        ? "B63E4F58C164950B41C07071B3CF6797B35ACC597B9476836FCA66E8A658E3FD"
        : "7986B5DC0CB3C2207ECE857D0060475A603B5342649D146874850C0D4A2509CA";
    if (datHash != expectedDat)
        throw new InvalidDataException("Rebuilt DAT is not byte-identical to frozen M194 expected SHA-256.");

    // Nothing is written until EVERY profile, checksum and release gate passes.
    Directory.CreateDirectory(parent.FullName);
    string temp = Path.Combine(parent.FullName,
        ".finesse-m195-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(temp);
        string infOut = Path.Combine(temp, "u0206030.inf");
        string datFolder = Path.Combine(temp, "B026778");
        Directory.CreateDirectory(datFolder);
        string datOut = Path.Combine(datFolder, "amdgcf.dat");
        File.WriteAllBytes(infOut, inf.CleanedInf);
        File.WriteAllBytes(datOut, dat);

        if (AmdInfSourceRebuilder.Sha256(File.ReadAllBytes(infOut)) != inf.CleanedInfSha256 ||
            AmdInfSourceRebuilder.Sha256(File.ReadAllBytes(datOut)) != expectedDat)
            throw new InvalidDataException("Staged output SHA-256 mismatch.");

        string manifest =
            inf.CleanedInfSha256 + "  u0206030.inf\r\n" +
            expectedDat + "  B026778/amdgcf.dat\r\n";
        File.WriteAllText(Path.Combine(temp, "SHA256SUMS.txt"),
            manifest, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(temp, "REVIEW-FIRST.txt"),
            "OFFLINE CANDIDATE ONLY: NOT SIGNED OR INSTALLABLE.\r\n" +
            "Profile: " + profile.Id + "\r\n" +
            "AMD release: 26.11.1\r\n" +
            "The source installer signature, 194-file inventory, INF2CAT, dual catalog trust, " +
            "device binding, AMD Software, recovery, and postboot checks have NOT been run.\r\n",
            new UTF8Encoding(false));
        Directory.Move(temp, outputRoot); // same-volume atomic directory rename
    }
    finally
    {
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
    }
    Console.WriteLine("M195_OFFLINE_SOURCE_REBUILD_PASS");
    Console.WriteLine("Profile=" + profile.Id);
    Console.WriteLine("INF_SHA256=" + inf.CleanedInfSha256);
    Console.WriteLine("DAT_SHA256=" + datHash);
    Console.WriteLine("OUTPUT=" + outputRoot);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("M195_OFFLINE_SOURCE_REBUILD_FAILED: " +
        ex.GetType().Name + ": " + ex.Message);
    return 1;
}
