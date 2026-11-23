using System.Text;
using Finesse.Compatibility;

/// <summary>
/// M195 development-only full OFFLINE preparation from official 26.11.1 AMD EXE.
/// This process NEVER runs pnputil, adds certificates, changes Windows driver
/// state, installs AMD Software, schedules restart or calls the live M194 GUI.
/// </summary>
try
{
    if (args.Length is not (12 or 14))
        throw new ArgumentException(
            "Usage: --installer PATH --extractor PINNED_7ZR_EXE --manifest PUBLIC_194_MANIFEST " +
            "--lenovo-policy AUTHORIZED_POLICY --profile Go1|GoS|Go2 --output-root NEW_DIRECTORY " +
            "[--inf2cat PINNED_MICROSOFT_INF2CAT_EXE]");
    var argsByName = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) ||
            !argsByName.TryAdd(args[i], args[i + 1]) ||
            string.IsNullOrWhiteSpace(args[i + 1]))
            throw new ArgumentException("Invalid/duplicate input: " + args[i]);
    }
    string Required(string name) => argsByName.TryGetValue(name, out string? value)
        ? value : throw new ArgumentException("Missing argument: " + name);
    if (argsByName.Keys.Any(key => key is not (
        "--installer" or "--extractor" or "--manifest" or
        "--lenovo-policy" or "--profile" or "--output-root" or "--inf2cat")))
        throw new ArgumentException("Unrecognized named option.");

    HandheldProfile profile = Required("--profile") switch
    {
        "Go1" => HandheldProfiles.Go1,
        "GoS" => HandheldProfiles.GoS,
        "Go2" => HandheldProfiles.Go2,
        _ => throw new ArgumentException("Unsupported exact device profile.")
    };
    string installer = Path.GetFullPath(Required("--installer"));
    string extractor = Path.GetFullPath(Required("--extractor"));
    string manifestPath = Path.GetFullPath(Required("--manifest"));
    string policyPath = Path.GetFullPath(Required("--lenovo-policy"));
    string output = Path.GetFullPath(Required("--output-root"));
    if (!File.Exists(manifestPath) || !File.Exists(policyPath))
        throw new FileNotFoundException("Original manifest and authorized policy are required.");
    if (Directory.Exists(output) || File.Exists(output))
        throw new IOException("Output root already exists; choose a fresh folder.");

    string manifest = await File.ReadAllTextAsync(manifestPath);
    string policy = await File.ReadAllTextAsync(policyPath, Encoding.Latin1);
    var phase1 = await Amd26111ArchiveExtraction.ExtractAsync(
        installer, extractor, Path.Combine(output, "OfficialOriginal"), manifest);
    Console.WriteLine("M195_VERIFIED_AMD_ORIGINAL_EXTRACTED");
    Console.WriteLine("OriginalInventorySha256=" + phase1.SourceManifestSha256);
    Console.WriteLine("OriginalFilesVerified=" + phase1.OriginalFilesVerified);
    Console.WriteLine("VerifiedExtractorExit=" + phase1.ExtractorExit);
    Console.WriteLine("OriginalExtractionEvidence=" + phase1.EvidenceFile);

    var phase2 = await Amd26111TwoPhasePreparation.VerifyAndStageAsync(
        installer, phase1.DisplayRoot, manifest, policy, profile,
        Path.Combine(output, "UnsignedCandidate"));
    if (phase2.UnsignedStage.SignedOrInstallable)
        throw new InvalidDataException("UNEXPECTED_INSTALLABLE_PREPARATION_RESULT");

    // Optional offline unsigned catalog generation. Only the exact pinned
    // Microsoft Inf2Cat is accepted. It still DOES NOT sign or install.
    if (argsByName.TryGetValue("--inf2cat", out string? inf2cat))
    {
        Amd26111UnsignedCatalogGenerator.Result generated =
            await Amd26111UnsignedCatalogGenerator.GenerateAsync(
                phase2.UnsignedStage, phase2.Candidate, manifest, inf2cat);
        Console.WriteLine("UnsignedMainCatalogSHA256=" + generated.Sha256);
        Console.WriteLine("UnsignedMainCatalogPath=" + generated.UnsignedCatalogPath);
        Console.WriteLine("UnsignedCatalogHasSigners=" + generated.CatalogContainsSigners);
    }

    Console.WriteLine("M195_SOURCE_FIRST_UNSIGNED_PREPARATION_PASS");
    Console.WriteLine("Profile=" + phase2.Candidate.Profile.Id);
    Console.WriteLine("InstallerSHA256=" + phase2.Installer.Sha256);
    Console.WriteLine("PreparedInfSHA256=" + phase2.Candidate.InfSha256);
    Console.WriteLine("PreparedDatSHA256=" + phase2.Candidate.DatSha256);
    Console.WriteLine("CandidateInventorySHA256=" + phase2.UnsignedStage.CandidateInventorySha256);
    Console.WriteLine("PreparedFiles=" + phase2.UnsignedStage.VerifiedFiles);
    Console.WriteLine("UnsignedCandidatePath=" + phase2.UnsignedStage.PackageRoot);
    Console.WriteLine("WARNING: the output is UNSIGNED and NOT installable.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("M195_OFFICIAL_SOURCE_PREPARATION_FAILED: " +
        ex.GetType().Name + ": " + ex.Message);
    Console.Error.WriteLine("NO driver installation or Windows trust changes were attempted.");
    return 1;
}
