using System.Text;
using Finesse.Compatibility;

// Offline-only M195 integration harness for source-first original AMD package
// acquisition -> exact INF/DAT candidate -> 194-file unsigned staging.
//
// The caller supplies an official AMD 26.11.1 EXE and its isolated original
// extraction. The tool verifies the EXE signature + pinned source identity,
// then independently verifies all 194 original extracted files.
// Never feed its output to pnputil without the remaining signing/trust lifecycle.
try
{
    if (args.Length != 12)
        throw new ArgumentException(
            "Usage: --installer EXACT_OFFICIAL_AMD_26_11_1_EXE --display-root ORIGINAL_AMD_WT6A_INF " +
            "--source-manifest PINNED_194_SHA256_MANIFEST " +
            "--lenovo-policy VERIFIED_POLICY_ROWS " +
            "--profile Go1|GoS|Go2 --output-dir NEW_DIRECTORY");

    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
            !options.TryAdd(args[i], args[i + 1]) ||
            string.IsNullOrWhiteSpace(args[i + 1]))
            throw new ArgumentException("Unrecognized/duplicate/empty argument: " + args[i]);
    }
    string Get(string key) => options.TryGetValue(key, out string? value)
        ? value : throw new ArgumentException("Missing option: " + key);
    if (options.Keys.Any(k => k is not (
        "--display-root" or "--source-manifest" or "--lenovo-policy" or
        "--profile" or "--output-dir" or "--installer")))
        throw new ArgumentException("Unknown argument supplied.");

    HandheldProfile profile = Get("--profile") switch
    {
        "Go1" => HandheldProfiles.Go1,
        "GoS" => HandheldProfiles.GoS,
        "Go2" => HandheldProfiles.Go2,
        _ => throw new ArgumentException("Unsupported exact hardware profile")
    };
    string installerFile = Path.GetFullPath(Get("--installer"));
    string sourceRoot = Path.GetFullPath(Get("--display-root"));
    string policyPath = Path.GetFullPath(Get("--lenovo-policy"));
    string manifestPath = Path.GetFullPath(Get("--source-manifest"));
    string destination = Path.GetFullPath(Get("--output-dir"));
    if (!Directory.Exists(sourceRoot) ||
        !File.Exists(policyPath) || !File.Exists(manifestPath))
        throw new FileNotFoundException("The original AMD extraction, policy and source manifest are required.");

    // One source-first entry point is shared between this offline CLI and
    // the future GUI acquisition layer. No signing or installation here.
    string policyRows = File.ReadAllText(policyPath, Encoding.Latin1);
    string sourceManifest = File.ReadAllText(manifestPath, Encoding.UTF8);
    Amd26111TwoPhasePreparation.PreparationReceipt outcome =
        await Amd26111TwoPhasePreparation.VerifyAndStageAsync(
            installerFile, sourceRoot, sourceManifest, policyRows, profile, destination);
    var candidate = outcome.Candidate;
    var staged = outcome.UnsignedStage;
    Console.WriteLine("VerifiedOfficialAmdInstallerSHA256=" + outcome.Installer.Sha256);
    Console.WriteLine("VerifiedWindowsSigner=" + outcome.Installer.SignerSubject);

    Console.WriteLine("M195_SOURCE_FIRST_UNSIGNED_STAGE_PASS");
    Console.WriteLine("Profile=" + candidate.Profile.Id);
    Console.WriteLine("SourceInventorySHA256=" + staged.OriginalInventorySha256);
    Console.WriteLine("CandidateInventorySHA256=" + staged.CandidateInventorySha256);
    Console.WriteLine("OriginalCatalogSHA256=" + staged.OriginalCatalogSha256);
    Console.WriteLine("VerifiedFiles=" + staged.VerifiedFiles);
    Console.WriteLine("SignedOrInstallable=" + staged.SignedOrInstallable);
    Console.WriteLine("Evidence=" + staged.EvidencePath);
    Console.WriteLine("PreparedFolder=" + staged.PackageRoot);
    Console.WriteLine("WARNING: UNSIGNED, no driver stage/bind or trust validation performed.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine("M195_SOURCE_FIRST_STAGE_FAILED: " +
        error.GetType().Name + ": " + error.Message);
    return 1;
}
