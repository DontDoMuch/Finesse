using System.Text;
using Finesse.Compatibility;

// Offline-only M195 integration harness for source-first original AMD package
// acquisition -> exact INF/DAT candidate -> 194-file unsigned staging.
//
// The caller must have already extracted the trusted, authentic official AMD
// 26.11.1 display package. This tool does NOT authenticate an AMD EXE signature;
// source provenance must be checked separately by the Windows acquisition layer.
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

    // The original AMD installer must pass its EXACT frozen 26.11.1 size,
    // SHA-256, Windows WinVerifyTrust and signer checks first. This does not
    // execute the installer or imply the already-extracted folder is trusted:
    // all 194 original files are independently checked below.
    OfficialAmdInstallerVerifier.VerifiedSource verifiedInstaller =
        await OfficialAmdInstallerVerifier.Verify26111Async(installerFile);
    Console.WriteLine("VerifiedOfficialAmdInstallerSHA256=" + verifiedInstaller.Sha256);
    Console.WriteLine("VerifiedWindowsSigner=" + verifiedInstaller.SignerSubject);

    // Only original AMD inputs are read; no patched vendor data is embedded.
    OfficialAmd26111SourceContract contract =
        OfficialAmd26111SourceContract.VerifiedFrozenM194;
    byte[] officialInf = File.ReadAllBytes(Path.Combine(sourceRoot, contract.OriginalInfRelative));
    byte[] officialDat = File.ReadAllBytes(Path.Combine(sourceRoot,
        contract.OriginalDatRelative.Replace('/', Path.DirectorySeparatorChar)));
    string policyRows = File.ReadAllText(policyPath, Encoding.Latin1);
    string sourceManifest = File.ReadAllText(manifestPath, Encoding.UTF8);
    Amd26111CandidateRecipe.VerifiedCandidate candidate = Amd26111CandidateRecipe.Build(
        officialInf, officialDat, policyRows, profile);

    // Requires a pinned 194-file source manifest and preserves original main
    // AMD catalog outside PreparedDriver. All source hashes checked BEFORE
    // anything is copied, and only 194 unsigned files are staged.
    AmdSourceFirstStager.UnsignedStageResult staged =
        await AmdSourceFirstStager.StagePinned26111Async(
            sourceRoot, destination, sourceManifest, candidate);

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
