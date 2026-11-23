using System.Security.Cryptography;
using System.Text;
using Finesse.Compatibility;

// Offline, file-only compatibility-recipe reproduction.
// No privileged operations; does not sign, stage or install a GPU driver.
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
    Amd26111CandidateRecipe.VerifiedCandidate verified =
        Amd26111CandidateRecipe.Build(sourceInf, sourceDat, policy, profile);
    string datHash = verified.DatSha256;
    string expectedDat = verified.DatSha256;

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
        File.WriteAllBytes(infOut, verified.InfBytes);
        File.WriteAllBytes(datOut, verified.DatBytes);

        if (AmdInfSourceRebuilder.Sha256(File.ReadAllBytes(infOut)) != verified.InfSha256 ||
            AmdInfSourceRebuilder.Sha256(File.ReadAllBytes(datOut)) != expectedDat)
            throw new InvalidDataException("Staged output SHA-256 mismatch.");

        string manifest =
            verified.InfSha256 + "  u0206030.inf\r\n" +
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
    Console.WriteLine("INF_SHA256=" + verified.InfSha256);
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
