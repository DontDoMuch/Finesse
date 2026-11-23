using System.Security.Cryptography;
using Finesse.Compatibility;

// Vendor-independent offline verification tool. Reads a DAT the caller has
// legitimately acquired from an original AMD installer. NEVER installs a GPU
// driver, changes signing/trust, toggles Secure Boot, or contacts the network.
try
{
    if (args.Length != 12)
        throw new ArgumentException("Usage: --input ORIGINAL_DAT --output CANDIDATE_DAT --profile Go1|GoS|Go2 --release EXACT_AMD_MARKER --original-sha256 SHA256 --expected-sha256 SHA256");

    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (int i = 0; i < args.Length; i += 2)
    {
        string option = args[i];
        if (!option.StartsWith("--", StringComparison.Ordinal) ||
            !options.TryAdd(option, args[i + 1]) ||
            string.IsNullOrWhiteSpace(args[i + 1]))
            throw new ArgumentException("Duplicate, invalid or empty option: " + option);
    }

    string Read(string key) => options.TryGetValue(key, out string? value)
        ? value : throw new ArgumentException("Missing option: " + key);

    HandheldProfile profile = Read("--profile") switch
    {
        "Go1" => HandheldProfiles.Go1,
        "GoS" => HandheldProfiles.GoS,
        "Go2" => HandheldProfiles.Go2,
        _ => throw new ArgumentException("Unsupported exact hardware profile")
    };

    string originalPath = Path.GetFullPath(Read("--input"));
    string outputPath = Path.GetFullPath(Read("--output"));
    string releaseMarker = Read("--release");
    string originalSha = Read("--original-sha256").ToUpperInvariant();
    string targetSha = Read("--expected-sha256").ToUpperInvariant();

    static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);
    if (!IsSha256(originalSha) || !IsSha256(targetSha))
        throw new ArgumentException("Expected full 64-character SHA-256 hashes.");

    if (string.Equals(originalPath, outputPath, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Input and output paths must be different.");
    if (!File.Exists(originalPath))
        throw new FileNotFoundException("Original DAT not found.", originalPath);

    byte[] original = File.ReadAllBytes(originalPath);
    string originalComputed = Convert.ToHexString(SHA256.HashData(original));
    if (!string.Equals(originalSha, originalComputed, StringComparison.Ordinal))
        throw new InvalidDataException("Original AMD DAT SHA-256 mismatch. Refusing output.");

    // The rebuilder validates the release-specific MetroHash of the ORIGINAL
    // file before producing new data, then round-trip-validates the result.
    byte[] revised = AmdDatReleaseRebuilder.BuildForVerifiedProfile(
        original, releaseMarker, profile);
    string actualSha = Convert.ToHexString(SHA256.HashData(revised));
    if (!string.Equals(targetSha, actualSha, StringComparison.Ordinal))
        throw new InvalidDataException(
            "Rebuilt DAT did not match the expected SHA-256. Refusing output. Actual=" + actualSha);

    if (File.Exists(outputPath))
    {
        byte[] existing = File.ReadAllBytes(outputPath);
        if (existing.AsSpan().SequenceEqual(revised))
        {
            Console.WriteLine("PASS: Matching output already exists; no changes made.");
            return 0;
        }
        throw new IOException("Output file already exists with different bytes; refusing overwrite.");
    }

    string directory = Path.GetDirectoryName(outputPath)
        ?? throw new InvalidDataException("Output must have a parent directory.");
    Directory.CreateDirectory(directory);
    string stagedPath = Path.Combine(directory, Path.GetRandomFileName() + ".tmp");
    try
    {
        File.WriteAllBytes(stagedPath, revised);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(stagedPath))) != targetSha)
            throw new InvalidDataException("Staged file changed before final move.");
        File.Move(stagedPath, outputPath);
    }
    finally
    {
        if (File.Exists(stagedPath))
            File.Delete(stagedPath);
    }
    Console.WriteLine("PASS: Independently reproduced expected DAT for " + profile.Id);
    Console.WriteLine("SHA256=" + actualSha);
    Console.WriteLine("OUTPUT=" + outputPath);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine("FAIL: " + exception.GetType().Name + ": " + exception.Message);
    return 1;
}
