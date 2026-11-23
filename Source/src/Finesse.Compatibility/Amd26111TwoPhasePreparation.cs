namespace Finesse.Compatibility;

/// <summary>
/// The public M195 preparation boundary intended to replace the *ordering*
/// of frozen M194's M80Recipe.LoadForM194-before-download dependency.
///
/// Step 1: authenticate the exact original AMD EXE with Windows trust and
/// frozen file identity. Step 2: validate already-extracted official source,
/// construct pinned INF/DAT in memory, and prepare a 194-file UNSIGNED folder.
///
/// Extraction itself is deliberately outside this method: the caller must
/// extract the authenticated installer with a separately reviewed extractor,
/// into an isolated directory, then supply that directory. All 194 original
/// files are SHA-pinned independently, preventing mismatched extraction.
/// </summary>
public static class Amd26111TwoPhasePreparation
{
    public sealed record PreparationReceipt(
        OfficialAmdInstallerVerifier.VerifiedSource Installer,
        Amd26111CandidateRecipe.VerifiedCandidate Candidate,
        AmdSourceFirstStager.UnsignedStageResult UnsignedStage);

    public static async Task<PreparationReceipt> VerifyAndStageAsync(
        string officialAmdInstallerPath,
        string extractedDisplayRoot,
        string original194Manifest,
        string authorizedLenovoRegistryPolicy,
        HandheldProfile exactProfile,
        string freshOutputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original194Manifest);
        ArgumentNullException.ThrowIfNull(authorizedLenovoRegistryPolicy);
        ArgumentNullException.ThrowIfNull(exactProfile);
        if (HandheldProfiles.Resolve(exactProfile.HardwareId) != exactProfile)
            throw new InvalidDataException("M195_EXACT_HARDWARE_PROFILE_REQUIRED");

        // The existing public installer cannot proceed on unsigned or merely
        // name-matching AMD files. This is a REAL WinVerifyTrust check.
        OfficialAmdInstallerVerifier.VerifiedSource source =
            await OfficialAmdInstallerVerifier.Verify26111Async(
                officialAmdInstallerPath, cancellationToken);

        var contract = OfficialAmd26111SourceContract.VerifiedFrozenM194;
        string root = Path.GetFullPath(extractedDisplayRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("M195_OFFICIAL_DISPLAY_ROOT_NOT_EXTRACTED");
        string infPath = Path.Combine(root,
            contract.OriginalInfRelative.Replace('/', Path.DirectorySeparatorChar));
        string datPath = Path.Combine(root,
            contract.OriginalDatRelative.Replace('/', Path.DirectorySeparatorChar));
        // The staging layer requires the whole 194-file inventory, including
        // the original original INF and DAT. Validate manifest identity first.
        _ = Amd26111SourceInventory.ParsePinned26111(original194Manifest);
        if (!File.Exists(infPath) || !File.Exists(datPath))
            throw new FileNotFoundException("M195_ORIGINAL_AMD_INF_OR_DAT_MISSING");

        byte[] originalInf = await File.ReadAllBytesAsync(infPath, cancellationToken);
        byte[] originalDat = await File.ReadAllBytesAsync(datPath, cancellationToken);
        Amd26111CandidateRecipe.VerifiedCandidate candidate =
            Amd26111CandidateRecipe.Build(
                originalInf, originalDat, authorizedLenovoRegistryPolicy, exactProfile);

        AmdSourceFirstStager.UnsignedStageResult staged =
            await AmdSourceFirstStager.StagePinned26111Async(
                root, freshOutputDirectory, original194Manifest, candidate, cancellationToken);
        if (staged.VerifiedFiles != contract.SelectedFileCount || staged.SignedOrInstallable)
            throw new InvalidDataException("M195_UNEXPECTED_SIGNED_OR_INCOMPLETE_STAGE");

        return new PreparationReceipt(source, candidate, staged);
    }
}
