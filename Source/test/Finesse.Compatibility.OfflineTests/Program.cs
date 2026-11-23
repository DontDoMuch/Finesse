using System.Buffers.Binary;
using Finesse.Compatibility;
using L = Finesse.Compatibility.M195LifecycleSequence;

// Vendor-independent synthetic fixtures. These tests do not assert a valid
// release-specific MetroHash header or GPU driver installation success.
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition)
        throw new InvalidOperationException("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}

void Reject(Action action, string name)
{
    try
    {
        action();
    }
    catch (InvalidDataException)
    {
        Console.WriteLine("PASS: " + name);
        passed++;
        return;
    }
    throw new InvalidOperationException("FAIL (accepted invalid data): " + name);
}

byte[] Fixture(params DatDeviceRecord[] rows)
{
    byte[] data = new byte[12 + rows.Length * 3];
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0, 4), (uint)rows.Length);
    BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(4, 8), 0x0123456789ABCDEFul);
    AmdDatRecordCodec.EncodeRecordPayload(rows).CopyTo(data, 12);
    return data;
}

Check(HandheldProfiles.Resolve(HandheldProfiles.Go1.HardwareId)?.Id == HandheldProfiles.Go1.Id, "Go1 exact profile");
Check(HandheldProfiles.Resolve(HandheldProfiles.GoS.HardwareId.ToLowerInvariant())?.Id == HandheldProfiles.GoS.Id, "Go S case-insensitive exact profile");
Check(HandheldProfiles.Resolve(HandheldProfiles.Go2.HardwareId)?.Id == HandheldProfiles.Go2.Id, "Go2 exact profile");
Check(HandheldProfiles.Resolve(@"PCI\VEN_1002&DEV_15BF&SUBSYS_380C17AA&REV_05") is null, "reject Go S wrong revision");
Check(HandheldProfiles.Resolve(@"PCI\VEN_1002&DEV_150E&SUBSYS_381C17AA&REV_C4") is null, "reject Go2 wrong revision");
Check(HandheldProfiles.Resolve(@"PCI\VEN_1002&DEV_15BF&SUBSYS_381217AA&REV_05") is null, "reject Go1 wrong revision");
Check(HandheldProfiles.Resolve("  ") is null, "reject empty hardware ID");
Check(HandheldProfiles.Go1.ExpectedLenovoPolicyCount == 26 &&
      HandheldProfiles.GoS.ExpectedLenovoPolicyCount == 30 &&
      HandheldProfiles.Go2.ExpectedLenovoPolicyCount == 28, "M194 Lenovo policy counts");

var a = new DatDeviceRecord(0x15BF, 0x04);
var b = new DatDeviceRecord(0x15C8, 0xC9);
byte[] original = Fixture(new DatDeviceRecord(0x150E, 0xC5), b);
var decoded = AmdDatRecordCodec.Decode(original);
Check(decoded.Count == 2 && decoded.Records[0] == new DatDeviceRecord(0x150E, 0xC5), "decode positional device");
Check(decoded.OriginalReleaseHeader == 0x0123456789ABCDEFul, "preserve header as opaque unverified identity");
var merged = AmdDatRecordCodec.Merge(decoded, [a, b]);
Check(merged.Count == 3 && merged[0] == new DatDeviceRecord(0x150E, 0xC5) &&
      merged[1] == a && merged[2] == b, "deterministic sort/deduplicate");
byte[] payload = AmdDatRecordCodec.EncodeRecordPayload(merged);
byte[] synthetic = new byte[12 + payload.Length];
BinaryPrimitives.WriteUInt32LittleEndian(synthetic.AsSpan(0, 4), (uint)merged.Count);
BinaryPrimitives.WriteUInt64BigEndian(synthetic.AsSpan(4, 8), 0x0123456789ABCDEFul);
payload.CopyTo(synthetic, 12);
var roundtrip = AmdDatRecordCodec.Decode(synthetic);
Check(roundtrip.Records.SequenceEqual(merged), "payload full roundtrip");
Check(!payload.AsSpan(2 * AmdDatRecordCodec.RecordBytes, 2)
        .SequenceEqual(original.AsSpan(AmdDatRecordCodec.HeaderBytes + AmdDatRecordCodec.RecordBytes, 2)),
      "positional re-encoding after merge");

Reject(() => AmdDatRecordCodec.Decode(new byte[11]), "reject short header");
Reject(() => AmdDatRecordCodec.Decode(new byte[12]), "reject zero count");
var wrongLength = (byte[])original.Clone();
BinaryPrimitives.WriteUInt32LittleEndian(wrongLength.AsSpan(0, 4), 3);
Reject(() => AmdDatRecordCodec.Decode(wrongLength), "reject count/length mismatch");
var badPosition = (byte[])original.Clone();
badPosition[12] = 0; badPosition[13] = 0;
Reject(() => AmdDatRecordCodec.Decode(badPosition), "reject invalid positional device");
Reject(() => AmdDatRecordCodec.EncodeRecordPayload([b, a]), "reject unsorted encoder input");
Reject(() => AmdDatRecordCodec.EncodeRecordPayload([a, a]), "reject duplicate encoder input");


const string copyCp = "CopyINF = .\\amdcp\\amdcp.inf\r\n";
const string copyUw = "CopyINF=amduw23e.inf\r\n";
var verified26111 = new[]
{
    new VerifiedCopyInfRemoval(copyCp, 11),
    new VerifiedCopyInfRemoval(copyUw, 11)
};
foreach (HandheldProfile profile in new[] { HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2 })
{
    string suffix = profile.Id == HandheldProfiles.Go1.Id ? "Go1" :
                    profile.Id == HandheldProfiles.GoS.Id ? "GoS" : "Go2";
    string prefix = "[" + profile.InstallSection + "]\r\n" +
                    profile.HardwareId + "\r\n" +
                    "LegionGo_Finesse_" + suffix + "_Policy\r\n";
    string syntheticInf = prefix + string.Concat(Enumerable.Repeat(copyCp, 11)) +
                          string.Concat(Enumerable.Repeat(copyUw, 11)) + "; untouched\r\n";
    string cleaned = AmdInfCopyInfClosure.RemoveVerifiedDependencies(
        syntheticInf, profile, verified26111);
    Check(cleaned == prefix + "; untouched\r\n", "exact M194 CopyINF removal " + profile.Id);
    Check(AmdInfCopyInfClosure.RemoveVerifiedDependencies(
        syntheticInf, profile, Array.Empty<VerifiedCopyInfRemoval>()) == syntheticInf,
        "future version keeps CopyINF unless explicitly approved " + profile.Id);
    Reject(() => AmdInfCopyInfClosure.RemoveVerifiedDependencies(
        syntheticInf, profile, [new VerifiedCopyInfRemoval(copyCp, 10)]),
        "reject wrong directive count " + profile.Id);
    Reject(() => AmdInfCopyInfClosure.RemoveVerifiedDependencies(
        syntheticInf, profile,
        [new VerifiedCopyInfRemoval(copyCp, 11), new VerifiedCopyInfRemoval(copyCp, 11)]),
        "reject duplicate removal contract " + profile.Id);
    Reject(() => AmdInfCopyInfClosure.RemoveVerifiedDependencies(
        syntheticInf.Replace("LegionGo_Finesse_" + suffix + "_Policy", "UNRELATED"),
        profile, verified26111),
        "reject altered profile policy " + profile.Id);
}


Check(AmdDatReleaseRebuilder.MetroHash64(Array.Empty<byte>()) == 0x705FB008071E967Dul,
    "MetroHash64 reference empty vector");
Check(AmdDatReleaseRebuilder.MetroHash64(System.Text.Encoding.ASCII.GetBytes("hello")) ==
      0x3F6FEA196D1F90FCul, "MetroHash64 reference hello vector");

const string release26111 = "26.20.15.16-261109a-206030E-AMD-Software-Adrenalin-Edition";
byte[] cleanOriginal = Fixture(new DatDeviceRecord(0x1100, 0x00));
BinaryPrimitives.WriteUInt64BigEndian(cleanOriginal.AsSpan(4, 8),
    AmdDatReleaseRebuilder.ComputeReleaseHeader(
        cleanOriginal.AsSpan(AmdDatRecordCodec.HeaderBytes), release26111));
Check(AmdDatReleaseRebuilder.VerifyOriginal(cleanOriginal, release26111).Count == 1,
    "release-specific checksum validates original DAT");

foreach (HandheldProfile profile in new[] { HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2 })
{
    byte[] rebuilt = AmdDatReleaseRebuilder.BuildForVerifiedProfile(cleanOriginal, release26111, profile);
    DatRecordAnalysis checkedBuilt = AmdDatReleaseRebuilder.VerifyOriginal(rebuilt, release26111);
    Check(checkedBuilt.Count == 3 &&
          checkedBuilt.Records.Contains(new DatDeviceRecord(profile.DeviceId, profile.Revision)) &&
          checkedBuilt.Records.Contains(new DatDeviceRecord(0x15C8, 0xC9)) &&
          checkedBuilt.Records.Contains(new DatDeviceRecord(0x1100, 0x00)),
          "full DAT rehash, 3 records, all rows preserved " + profile.Id);
    byte[] repeated = AmdDatReleaseRebuilder.BuildForVerifiedProfile(rebuilt, release26111, profile);
    Check(repeated.SequenceEqual(rebuilt), "idempotent DAT reconstruction " + profile.Id);
}
Reject(() => AmdDatReleaseRebuilder.VerifyOriginal(cleanOriginal, "WRONG_RELEASE_STRING"),
    "reject incorrect release MetroHash marker");
var tamperedChecksum = (byte[])cleanOriginal.Clone();
tamperedChecksum[6] ^= 0x01;
Reject(() => AmdDatReleaseRebuilder.VerifyOriginal(tamperedChecksum, release26111),
    "reject modified original DAT checksum");
var tamperedRecord = (byte[])cleanOriginal.Clone();
tamperedRecord[14] ^= 0x01;
Reject(() => AmdDatReleaseRebuilder.VerifyOriginal(tamperedRecord, release26111),
    "reject modified original DAT record with stale header");
Reject(() => AmdDatReleaseRebuilder.BuildForVerifiedProfile(cleanOriginal, release26111,
    HandheldProfiles.Go2 with { HardwareId = HandheldProfiles.Go1.HardwareId }),
    "reject spoofed/modified profile object");


// M195 source-driven INF clone tests use entirely SYNTHETIC AMD-like sections.
// No actual AMD or Lenovo source file is checked in.
string SyntheticOriginalInf(string family)
{
    var b = new System.Text.StringBuilder();
    b.Append("[ATI.Mfg.NTamd64.10.0.1..19044]\r\n");
    b.Append("\"%EXAMPLE%\" = ati2mtag_Unrelated, PCI\\VEN_FAKE\r\n");
    b.Append("\r\n[ati2mtag_" + family + "]\r\n");
    b.Append("FeatureScore=CF\r\n");
    b.Append("CopyINF = .\\amdcp\\amdcp.inf\r\n");
    b.Append("CopyINF=amduw23e.inf\r\n\r\n");
    foreach (string suffix in new[] { ".Services", ".HW", ".Software", ".Components", ".GeneralConfigData" })
    {
        b.Append("[ati2mtag_" + family + suffix + "]\r\n");
        if (suffix != ".Software")
            b.Append("ExampleOfflineOnly=1\r\n");
        b.Append("\r\n");
    }
    b.Append("[ati2mtag_Another]\r\n");
    for (int i = 0; i < 9; i++)
        b.Append("CopyINF = .\\amdcp\\amdcp.inf\r\nCopyINF=amduw23e.inf\r\n");
    return b.ToString();
}

foreach (HandheldProfile profile in new[] { HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2 })
{
    string policy = string.Join("\r\n",
        Enumerable.Range(0, profile.ExpectedLenovoPolicyCount)
            .Select(i => $"HKR,,TestFinesseSetting{i},%REG_DWORD%,{i}")) + "\r\n";
    string sourceInf = SyntheticOriginalInf(profile.Family);
    byte[] rebuiltInf = AmdInfSourceRebuilder.BuildProfileInf(
        System.Text.Encoding.Latin1.GetBytes(sourceInf), profile, policy);
    string normalizedInf = System.Text.Encoding.Latin1.GetString(rebuiltInf);
    Check(normalizedInf.Contains($"[{profile.InstallSection}]\r\n", StringComparison.Ordinal) &&
          normalizedInf.Contains(profile.HardwareId, StringComparison.Ordinal) &&
          normalizedInf.Contains("AddReg = LegionGo_Finesse_", StringComparison.Ordinal),
          "clone official-family sections without embedded vendor INF " + profile.Id);
    Check(normalizedInf.Split(
        ["CopyINF = .\\amdcp\\amdcp.inf\r\n"], StringSplitOptions.None).Length - 1 == 11 &&
          normalizedInf.Split(
        ["CopyINF=amduw23e.inf\r\n"], StringSplitOptions.None).Length - 1 == 11,
        "copyinf synthetic pre-cleanup exact 11+11 " + profile.Id);

    string closed = AmdInfCopyInfClosure.RemoveVerifiedDependencies(normalizedInf, profile,
        [new VerifiedCopyInfRemoval("CopyINF = .\\amdcp\\amdcp.inf\r\n", 11),
         new VerifiedCopyInfRemoval("CopyINF=amduw23e.inf\r\n", 11)]);
    Check(!closed.Contains("CopyINF = .\\amdcp\\amdcp.inf", StringComparison.Ordinal) &&
          !closed.Contains("CopyINF=amduw23e.inf", StringComparison.Ordinal) &&
          closed.Contains("ExampleOfflineOnly=1", StringComparison.Ordinal),
          "source-driven INF closure preserves unrelated directives " + profile.Id);

    Reject(() => AmdInfSourceRebuilder.BuildProfileInf(
        System.Text.Encoding.Latin1.GetBytes(sourceInf), profile, policy + "HKR,,Extra,%REG_DWORD%,1\r\n"),
        "reject unverified Lenovo policy count " + profile.Id);
    Reject(() => AmdInfSourceRebuilder.BuildProfileInf(
        System.Text.Encoding.Latin1.GetBytes(sourceInf.Replace(
            "[ati2mtag_" + profile.Family + ".HW]", "[ati2mtag_missing.HW]",
            StringComparison.Ordinal)), profile, policy),
        "reject absent AMD source section " + profile.Id);
    Reject(() => AmdInfSourceRebuilder.BuildProfileInf(
        System.Text.Encoding.Latin1.GetBytes(
            sourceInf.Replace("[ATI.Mfg.NTamd64.10.0.1..19044]\r\n",
                "[ATI.Mfg.NTamd64.10.0.1..19044]\r\n" +
                "[ATI.Mfg.NTamd64.10.0.1..19044]\r\n", StringComparison.Ordinal)),
        profile, policy), "reject duplicate manufacturer anchor " + profile.Id);
}


var officialContract = OfficialAmd26111SourceContract.VerifiedFrozenM194;
Check(officialContract.InstallerFileName ==
      "amd-software-adrenalin-edition-26.11.1-win11-c.exe" &&
      officialContract.ExactInstallerBytes == 1_780_861_240 &&
      officialContract.SelectedFileCount == 194,
      "official 26.11.1 source-only contract matches frozen M194");
Check(officialContract.OriginalInfSha256 ==
      "2E1125EB79359D3BAEB283B81868064257C5F1436D8928AC2EC123837DD70C57" &&
      officialContract.OriginalDatSha256 == Amd26111CandidateRecipe.OriginalDatSha256,
      "single source-of-truth original AMD INF/DAT hashes");
Check(!string.IsNullOrWhiteSpace(officialContract.ExpectedSignerName) &&
      officialContract.RootInInstallerArchive ==
          "Packages/Drivers/Display/WT6A_INF",
      "source-only contract includes verifier/extraction facts but no private files");
Reject(() => Amd26111CandidateRecipe.Build(
        System.Text.Encoding.Latin1.GetBytes(SyntheticOriginalInf("Phoenix")),
        cleanOriginal,
        string.Join("\r\n", Enumerable.Range(0, HandheldProfiles.Go1.ExpectedLenovoPolicyCount)
            .Select(i => $"HKR,,OfflineFixture{i},%REG_DWORD%,0")),
        HandheldProfiles.Go1),
    "reject synthetic untrusted DAT/INF from pinned candidate path");


// Vendor-independent 194-file source-first staging test. No AMD files.
string demoRoot = Path.Combine(Path.GetTempPath(),
    "Finesse-M195-SourceFirst-" + Guid.NewGuid().ToString("N"));
try
{
    string originals = Path.Combine(demoRoot, "OriginalAmdExtracted");
    Directory.CreateDirectory(originals);
    var fakeRows = new List<AmdSourceFileProof>();
    byte[] fakeInf = System.Text.Encoding.ASCII.GetBytes("OFFICIAL-INF-SYNTHETIC");
    byte[] fakeDat = System.Text.Encoding.ASCII.GetBytes("OFFICIAL-DAT-SYNTHETIC");
    byte[] fakeKernel = System.Text.Encoding.ASCII.GetBytes("OFFICIAL-KERNEL-SYNTHETIC");
    static string FakeHash(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
    void SourceFile(string relative, byte[] data)
    {
        string p = Path.Combine(originals,
            relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, data);
        fakeRows.Add(new AmdSourceFileProof(relative, FakeHash(data)));
    }
    SourceFile("u0206030.inf", fakeInf);
    SourceFile("B026778/amdgcf.dat", fakeDat);
    SourceFile("B026778/amdkmdag.sys", fakeKernel);
    for (int i = 0; i < 191; i++)
        SourceFile($"B026778/synthetic-{i:D3}.bin",
            System.Text.Encoding.ASCII.GetBytes($"TEST-FILE-{i:D3}"));
    File.WriteAllText(Path.Combine(originals, "u0206030.cat"),
        "SYNTHETIC-ORIGINAL-CATALOG");
    Check(fakeRows.Count == 194, "synthetic source inventory contains 194 original files");

    var sourceContract = officialContract with
    {
        OriginalInfSha256 = FakeHash(fakeInf),
        OriginalDatSha256 = FakeHash(fakeDat),
        OriginalKernelSha256 = FakeHash(fakeKernel)
    };
    string manifest = string.Concat(fakeRows
        .OrderByDescending(x => x.Relative, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.Sha256 + "  " + x.Relative + "\n"));
    string canonical = string.Concat(fakeRows
        .OrderBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
        .Select(x => x.Sha256 + "  " + x.Relative + "\n"));
    string pinnedManifest = FakeHash(System.Text.Encoding.ASCII.GetBytes(canonical));
    AmdVerifiedSourceInventory parsed =
        Amd26111SourceInventory.ParseAndVerify(manifest, sourceContract, pinnedManifest);
    Check(parsed.Entries.Count == 194 && parsed.CanonicalSha256 == pinnedManifest,
        "194 original source hashes verified independent of source manifest row order");

    byte[] fakeCandidateInf = System.Text.Encoding.ASCII.GetBytes("LOCAL-PATCHED-INF");
    byte[] fakeCandidateDat = System.Text.Encoding.ASCII.GetBytes("LOCAL-PATCHED-DAT");
    var fakeCandidate = new Amd26111CandidateRecipe.VerifiedCandidate(
        HandheldProfiles.Go1, fakeCandidateInf, fakeCandidateDat,
        FakeHash(fakeCandidateInf), FakeHash(fakeCandidateDat),
        Amd26111CandidateRecipe.ReleaseMarker);
    string expectedPrepared = Amd26111SourceInventory.CandidateManifestSha256(
        parsed, fakeCandidate);

    string outDir = Path.Combine(demoRoot, "Prepared");
    var preparedStage = await AmdSourceFirstStager.StageAsync(
        originals, outDir, parsed, fakeCandidate, expectedPrepared);
    Check(preparedStage.VerifiedFiles == 194 &&
          !preparedStage.SignedOrInstallable &&
          File.Exists(preparedStage.EvidencePath),
          "original-source 194-file preparation completed UNSIGNED");
    Check(Directory.EnumerateFiles(preparedStage.PackageRoot, "*",
              SearchOption.AllDirectories).Count() == 194 &&
          !File.Exists(Path.Combine(preparedStage.PackageRoot, "u0206030.cat")),
          "194 candidate files prepared; original main AMD catalog not installed as new main catalog");
    Check(File.ReadAllBytes(Path.Combine(preparedStage.PackageRoot, "u0206030.inf"))
              .SequenceEqual(fakeCandidateInf) &&
          File.ReadAllBytes(Path.Combine(preparedStage.PackageRoot, "B026778", "amdgcf.dat"))
              .SequenceEqual(fakeCandidateDat),
          "only candidate INF and DAT replaced after source verification");
    Check(File.ReadAllText(preparedStage.PreservedOriginalCatalog) ==
          "SYNTHETIC-ORIGINAL-CATALOG" &&
          File.ReadAllBytes(Path.Combine(originals, "u0206030.inf")).SequenceEqual(fakeInf),
          "original catalog separately preserved and original source never modified");
    try
    {
        await AmdSourceFirstStager.StageAsync(
            originals, outDir, parsed, fakeCandidate, expectedPrepared);
        throw new InvalidOperationException("FAIL: accepted pre-existing output directory");
    }
    catch (IOException)
    {
        Check(true, "refuse output overwrite");
    }
    var forgedCandidate = fakeCandidate with { InfBytes = fakeInf };
    Reject(() => AmdSourceFirstStager.StageAsync(originals,
        Path.Combine(demoRoot, "forged"), parsed, forgedCandidate,
        expectedPrepared).GetAwaiter().GetResult(),
        "reject candidate byte/hash mismatch before staging");
    Reject(() => AmdSourceFirstStager.StageAsync(originals,
        Path.Combine(originals, "hidden-output"), parsed, fakeCandidate,
        expectedPrepared).GetAwaiter().GetResult(),
        "reject writing staged candidate inside original AMD source");
    Reject(() => AmdSourceFirstStager.StageAsync(originals,
        Path.Combine(demoRoot, "wrong-manifest"), parsed, fakeCandidate,
        new string('0', 64)).GetAwaiter().GetResult(),
        "reject unapproved candidate inventory SHA-256");
    File.WriteAllText(Path.Combine(originals, "B026778", "amdkmdag.sys"),
        "TAMPERED-ORIGINAL-KERNEL");
    Reject(() => AmdSourceFirstStager.StageAsync(originals,
        Path.Combine(demoRoot, "tampered"), parsed, fakeCandidate,
        expectedPrepared).GetAwaiter().GetResult(),
        "reject modified original kernel before staging");
    Reject(() => Amd26111SourceInventory.ValidateRelative("../escape"),
        "reject source manifest path traversal");
    Reject(() => Amd26111SourceInventory.ValidateRelative("CON.dll"),
        "reject reserved Windows device name");
}
finally
{
    if (Directory.Exists(demoRoot))
        Directory.Delete(demoRoot, recursive: true);
}


// Public M194-derived original source metadata only: real 194 paths + SHA-256s.
// No original AMD package file content is committed or loaded by this test.
string publicManifest = Path.Combine(Directory.GetCurrentDirectory(),
    "Source", "contracts", "releases", "amd-26.11.1-original-194.sha256");
if (!File.Exists(publicManifest))
    publicManifest = Path.Combine(Directory.GetCurrentDirectory(),
        "contracts", "releases", "amd-26.11.1-original-194.sha256");
if (!File.Exists(publicManifest))
    throw new InvalidOperationException("M195_REQUIRED_PUBLIC_194_FILE_MANIFEST_MISSING");
string publishedInventoryText = File.ReadAllText(publicManifest);
AmdVerifiedSourceInventory frozenOriginal =
    Amd26111SourceInventory.ParsePinned26111(publishedInventoryText);
Check(frozenOriginal.Entries.Count == 194 &&
      frozenOriginal.CanonicalSha256 == Amd26111SourceInventory.PinnedOriginalManifestSha256,
      "published original M194 194-file name/hash manifest matches pinned canonical digest");

foreach (HandheldProfile profile in new[] {
    HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2
})
{
    string candidateInfHash = profile.Id switch
    {
        "LegionGo1" => "5F49F240CBDA41F7BD6E30867751507ACF38386B7AA3E0848639BCD002FE691A",
        "LegionGoS-Z1Extreme" => "E2B0809F3E3D135BFD03F1E0D97D1140BD6A05557024CE1D3EB604DFDAA90071",
        "LegionGo2-Z2Extreme" => "A355187F07FEEE5A0E0EDCC772A95C68E4F54B04323DE3D45634FFBE447CCBA3",
        _ => throw new InvalidOperationException("Unsupported fixture profile")
    };
    string candidateDatHash = profile.Id == "LegionGo2-Z2Extreme"
        ? "B63E4F58C164950B41C07071B3CF6797B35ACC597B9476836FCA66E8A658E3FD"
        : "7986B5DC0CB3C2207ECE857D0060475A603B5342649D146874850C0D4A2509CA";
    // These records intentionally carry no AMD file bytes: this test proves
    // hash-list identity only. Rebuilding bytes requires the authentic files.
    var manifestOnly = new Amd26111CandidateRecipe.VerifiedCandidate(
        profile, [], [], candidateInfHash, candidateDatHash,
        Amd26111CandidateRecipe.ReleaseMarker);
    Check(Amd26111SourceInventory.CandidateManifestSha256(
              frozenOriginal, manifestOnly) ==
          Amd26111SourceInventory.PinnedCandidateManifestHash(profile),
          "published original manifest maps to frozen candidate inventory " + profile.Id);
}
Reject(() => Amd26111SourceInventory.ParsePinned26111(
        publishedInventoryText.Replace(
            "2E1125EB79359D3BAEB283B81868064257C5F1436D8928AC2EC123837DD70C57  u0206030.inf",
            new string('0',64) + "  u0206030.inf", StringComparison.Ordinal)),
    "tampered published AMD original INF SHA cannot pass manifest pin");


// Windows AMD official source gate: do not permit filename, HTTPS origin, or
// plausible Authenticode identity to substitute for pinned file content.
// Tests create only tiny synthetic files, and never run the AMD installer.
string trustTestRoot = Path.Combine(Path.GetTempPath(),
    "Finesse-M195-OfficialTrust-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(trustTestRoot);
try
{
    string misleadingName = Path.Combine(trustTestRoot, "amd-unpinned.exe");
    await File.WriteAllTextAsync(misleadingName, "SYNTHETIC-NOT-AMD");
    Reject(() => OfficialAmdInstallerVerifier.Verify26111Async(misleadingName)
        .GetAwaiter().GetResult(), "reject AMD installer with unpinned filename");

    string pinnedName = Path.Combine(trustTestRoot,
        OfficialAmd26111SourceContract.VerifiedFrozenM194.InstallerFileName);
    await File.WriteAllTextAsync(pinnedName, "SYNTHETIC-NOT-AMD");
    Reject(() => OfficialAmdInstallerVerifier.Verify26111Async(pinnedName)
        .GetAwaiter().GetResult(),
        "reject correct AMD filename with wrong size BEFORE signature inspection");
    Check(new FileInfo(pinnedName).Length < 1024,
        "source verifier read-only; synthetic input not replaced");
}
finally
{
    Directory.Delete(trustTestRoot, recursive: true);
}


// Pinned extraction gate: the 7zr binary and official EXE must pass explicit
// frozen hashes before ANY extraction, and the public metadata selects 195
// original files, including one original AMD catalog.
Check(Amd26111ArchiveExtraction.Pinned7zrBytes == 602_624 &&
      Amd26111ArchiveExtraction.Pinned7zrSha256 ==
          "256FECA8E274E5DA655E2A284FABAFD9F554365EB164862089DACD4E8276D282",
      "verified original M194 7zr extractor length and sha256 contract");

string extractionProbe = Path.Combine(Path.GetTempPath(),
    "Finesse-M195-Extract-Probe-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(extractionProbe);
try
{
    string notAmd = Path.Combine(extractionProbe, "not-official.exe");
    File.WriteAllText(notAmd, "NOT-AMD");
    string fake7z = Path.Combine(extractionProbe, "7zr.exe");
    File.WriteAllText(fake7z, "NOT-7ZIP");
    string newWorkspace = Path.Combine(extractionProbe, "NewWorkspace");
    Reject(() => Amd26111ArchiveExtraction.ExtractAsync(notAmd, fake7z,
        newWorkspace, publishedInventoryText).GetAwaiter().GetResult(),
        "archive extraction rejects unpinned AMD source before running any tool");
    Check(!Directory.Exists(newWorkspace),
        "rejected archive source never creates extraction output");

    Reject(() => Amd26111TwoPhasePreparation.VerifyAndStageAsync(
        notAmd, extractionProbe, publishedInventoryText, "HKR,,Fixture,0x00010001,0",
        HandheldProfiles.Go1, newWorkspace).GetAwaiter().GetResult(),
        "two-phase preparation rejects unsigned original AMD installer first");
    Check(!Directory.Exists(newWorkspace),
        "two-phase source failure leaves no candidate package");
}
finally
{
    Directory.Delete(extractionProbe, recursive: true);
}


// Exact source-first 195-file extraction closure: only original AMD display
// files + the original main CAT, no private candidate or stale package.
string[] selectedOriginal = Amd26111ArchiveExtraction.BuildPinnedOriginalSelection(
    publishedInventoryText);
string expectedArchivePrefix = OfficialAmd26111SourceContract.VerifiedFrozenM194
    .RootInInstallerArchive.TrimEnd('/') + "/";
Check(selectedOriginal.Length == 195 &&
      selectedOriginal.All(x => x.StartsWith(expectedArchivePrefix,
          StringComparison.Ordinal)) &&
      selectedOriginal.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 195,
      "pinned original AMD 26.11.1 selective extraction has 195 unique archive paths");
Check(selectedOriginal.Count(x => x.Equals(expectedArchivePrefix + "u0206030.cat",
          StringComparison.OrdinalIgnoreCase)) == 1 &&
      selectedOriginal.Count(x => x.Equals(expectedArchivePrefix + "u0206030.inf",
          StringComparison.OrdinalIgnoreCase)) == 1 &&
      selectedOriginal.Count(x => x.Equals(expectedArchivePrefix + "B026778/amdgcf.dat",
          StringComparison.OrdinalIgnoreCase)) == 1,
      "195-file extraction selection includes original AMD catalog INF and DAT exactly once");
Reject(() => Amd26111ArchiveExtraction.BuildPinnedOriginalSelection(
        publishedInventoryText.Replace(
            "2E1125EB79359D3BAEB283B81868064257C5F1436D8928AC2EC123837DD70C57  u0206030.inf",
            new string('0', 64) + "  u0206030.inf", StringComparison.Ordinal)),
    "selection cannot be derived from tampered original AMD metadata");


// Official AMD network allowlist tests; these do not make HTTP requests.
const string pinnedReleaseName = "amd-software-adrenalin-edition-26.11.1-win11-c.exe";
Check(OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://drivers.amd.com/drivers/" + pinnedReleaseName)) &&
      OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://download.amd.com/drivers/" + pinnedReleaseName)),
      "allow only exact AMD official-host HTTPS release URL");
Check(!OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("http://drivers.amd.com/drivers/" + pinnedReleaseName)) &&
      !OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://evil.drivers.amd.com/drivers/" + pinnedReleaseName)) &&
      !OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://example.com/drivers/" + pinnedReleaseName)) &&
      !OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://user@drivers.amd.com/drivers/" + pinnedReleaseName)),
      "reject untrusted hosts, HTTP, and authenticated userinfo");
Check(!OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://drivers.amd.com/drivers/" + pinnedReleaseName + "?redirect=1")) &&
      !OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://drivers.amd.com/drivers/" + pinnedReleaseName + "#extra")) &&
      !OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://www.amd.com/Download-Incomplete.html")),
      "reject query strings, fragments, and AMD's download error HTML");
Check(!OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://drivers.amd.com/drivers/other-release.exe")) &&
      !OfficialAmd26111Downloader.IsApprovedDownloadUri(
        new Uri("https://drivers.amd.com:444/drivers/" + pinnedReleaseName)),
      "reject different release or nonstandard port");


// M195 lifecycle ordering contract: pure fake Windows adapters; never binds
// drivers, signs catalogs, installs software or reboots test runners.
var lifecycle = new FakeM195Lifecycle();
var lifecycleSteps = new List<L.Step>();
L.PendingPostboot pending = await L.RunPrebootAsync(lifecycle,
    (step, _) => lifecycleSteps.Add(step));
Check(lifecycleSteps.SequenceEqual(Enumerable.Range(1, 11)
          .Select(n => (L.Step)n)) &&
      lifecycle.Calls.Count(c => c == "Reboot") == 1,
      "12-step M194-derived lifecycle requests exactly one reboot after 11 ordered preboot steps");
Check(lifecycle.Calls.IndexOf("Software") < lifecycle.Calls.IndexOf("RegisterPostboot") &&
      lifecycle.Calls.IndexOf("RegisterPostboot") < lifecycle.Calls.IndexOf("Reboot") &&
      pending.PrebootCode43 &&
      pending.ExpectedDriverVersion ==
          OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion,
      "Code43 remains provisional; AMD Software precedes registration and single reboot");
L.PostbootOutcome postboot = await L.RunPostbootAsync(pending, lifecycle,
    (step, _) => lifecycleSteps.Add(step));
Check(lifecycleSteps.Count == 12 && lifecycleSteps[11] == L.Step.VerifyPostboot &&
      postboot.Status == L.PostbootState.TargetConfirmedCode0 &&
      postboot.TargetDriverConfirmed && lifecycle.Calls.IndexOf("ObserveFirstPostboot") >= 0 &&
      !lifecycle.Calls.Contains("RecoverPostboot"),
      "postboot target Code0 confirms only after a new boot and first observation");

var badSoftware = new FakeM195Lifecycle { FailSoftware = true };
Reject(() => L.RunPrebootAsync(badSoftware).GetAwaiter().GetResult(),
    "abort installation when AMD Software fails before any reboot");
Check(!badSoftware.Calls.Contains("RegisterPostboot") &&
      !badSoftware.Calls.Contains("Reboot") &&
      badSoftware.Calls.Contains("PrebootRollback"),
      "software failure restores original driver and never schedules reboot");

var incompleteM191Evidence = new FakeM195Lifecycle { DualTrustedCount = 13 };
L.PendingPostboot incompleteM191Pending = await L.RunPrebootAsync(incompleteM191Evidence);
Check(incompleteM191Pending.ExpectedDriverVersion ==
          OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion &&
      incompleteM191Evidence.Calls.Contains("Software") &&
      incompleteM191Evidence.Calls.Contains("Reboot") &&
      !incompleteM191Evidence.Calls.Contains("PrebootRollback"),
    "M194 optional M191 catalog evidence does not introduce an unconditional install blocker");

var badBaseline = new FakeM195Lifecycle { UseGo2 = true, VerifiedBaseline = false };
Reject(() => L.RunPrebootAsync(badBaseline).GetAwaiter().GetResult(),
    "Go2 tester requires verified existing-driver export before mutation");
Check(!badBaseline.Calls.Contains("Acquire") && !badBaseline.Calls.Contains("Reboot"),
      "missing tester recovery baseline stops before source acquisition");

var failedPostboot = new FakeM195Lifecycle { PostbootProblemCode = 43 };
L.PendingPostboot ticket = await L.RunPrebootAsync(failedPostboot);
var reverted = await L.RunPostbootAsync(ticket, failedPostboot);
Check(reverted.Status == L.PostbootState.TargetFailedRecovered &&
      !reverted.TargetDriverConfirmed &&
      failedPostboot.Calls.IndexOf("ObserveFirstPostboot") <
          failedPostboot.Calls.IndexOf("RecoverPostboot") &&
      failedPostboot.Calls.Count(c => c == "Reboot") == 1,
      "failed postboot target observed before recovery; recovery never called target success");

var noBoot = new FakeM195Lifecycle { PostbootIdentity = "boot-A" };
L.PendingPostboot beforeRealRestart = await L.RunPrebootAsync(noBoot);
Reject(() => L.RunPostbootAsync(beforeRealRestart, noBoot).GetAwaiter().GetResult(),
    "reject fake postboot success without real boot identity change");
Check(!noBoot.Calls.Contains("RecoverPostboot"),
      "no recovery happens without first confirmed postboot observation");


// Real Windows WinVerifyTrust positive case: confirm the native GUID pointer,
// trust policy, and signer extraction work on a known Microsoft-signed PE.
// This test makes NO Windows security or driver configuration changes.
if (OperatingSystem.IsWindows())
{
    // Inbox Windows binaries can be catalog-signed. Probe several Microsoft
    // PE applications, and require a real embedded Authenticode signer.
    string system = Environment.GetFolderPath(Environment.SpecialFolder.System);
    string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    var candidates = new[]
    {
        Path.Combine(programFiles, "dotnet", "dotnet.exe"),
        Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
        Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
        Path.Combine(system, "msiexec.exe"),
        Path.Combine(system, "winver.exe")
    }.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    Check(candidates.Length > 0, "Windows runner provides Microsoft PE trust test candidates");
    bool validatedEmbeddedMicrosoft = false;
    foreach (string pe in candidates)
    {
        var signature = OfficialAmdInstallerVerifier.InspectWindowsSignature(pe);
        Console.WriteLine("M195_NATIVE_WINTRUST_DIAGNOSTIC File=" + pe +
            " Status=0x" + unchecked((uint)signature.WindowsStatus).ToString("X8") +
            " Trusted=" + signature.Trusted +
            " Subject=" + signature.Subject +
            " EmbeddedSignerPresent=" + !string.IsNullOrWhiteSpace(signature.Thumbprint));
        if (signature.Trusted && signature.WindowsStatus == 0 &&
            signature.Subject.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(signature.Thumbprint))
            validatedEmbeddedMicrosoft = true;
    }
    Check(validatedEmbeddedMicrosoft,
        "native WinVerifyTrust validates an actual embedded Microsoft Authenticode signer");
}


// The Inf2Cat stage is a real OFFLINE tool, never automatically signed.
// Validate that its source gate refuses an incomplete 194-file package before
// attempting a WDK process or touching any Windows certificate store.
Check(Amd26111UnsignedCatalogGenerator.PinnedInf2CatSha256 ==
      "82C302FC9069783674B51665CE80E769F8500DB7FC737A4B8A3773C521950B86",
      "frozen M149 Microsoft Inf2Cat executable SHA-256 retained");
if (OperatingSystem.IsWindows())
{
    var rejectedUnsignedStage = new AmdSourceFirstStager.UnsignedStageResult(
        "UNCREATED_UNSIGNED_CANDIDATE_DIR", "UNCREATED_ORIGINAL_CAT",
        new string('A', 64), Amd26111SourceInventory.PinnedOriginalManifestSha256,
        Amd26111SourceInventory.PinnedCandidateManifestHash(HandheldProfiles.Go1),
        193, "NO_EVIDENCE", false);
    var markerOnly = new Amd26111CandidateRecipe.VerifiedCandidate(
        HandheldProfiles.Go1, [], [],
        "5F49F240CBDA41F7BD6E30867751507ACF38386B7AA3E0848639BCD002FE691A",
        "7986B5DC0CB3C2207ECE857D0060475A603B5342649D146874850C0D4A2509CA",
        Amd26111CandidateRecipe.ReleaseMarker);
    Reject(() => Amd26111UnsignedCatalogGenerator.GenerateAsync(
        rejectedUnsignedStage, markerOnly, publishedInventoryText,
        "NOT-A-VERIFIED-INF2CAT.EXE").GetAwaiter().GetResult(),
        "Inf2Cat never launches when original 194-file stage is incomplete");
}


// M195 REAL Windows device/inventory code, tested with fake data plus one
// read-only SetupAPI enumeration on the Windows GitHub runner. No live driver
// binding, pnputil export, certificate or reboot operation runs in these tests.
M195WindowsGpuInventory.ObservedDisplay DisplayFor(
    HandheldProfile p, string instance = "PCI\\TEST_INSTANCE",
    string inf = "oem42.inf", uint problem = 0, uint status = 8,
    string service = "amdkmdag", string provider = "Advanced Micro Devices") =>
    new(instance, [p.HardwareId], M195WindowsGpuInventory.DisplayClass,
        inf, provider, "32.0.32015.7029", service, status, problem);

foreach (HandheldProfile p in new[] {
    HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2
})
{
    var target = M195WindowsGpuInventory.RequireUniqueExactGpu([DisplayFor(p)]);
    Check(target.Profile == p &&
          M195WindowsGpuInventory.EligibleForRecoveryExport(target.Display),
          "native display inventory matches exact hardware and healthy OEM baseline " + p.Id);
    Reject(() => M195WindowsGpuInventory.RequireUniqueExactGpu(
        [DisplayFor(p, inf:"oem42.inf", problem: 0),
         DisplayFor(p, instance: "PCI\\SECOND_TEST_INSTANCE")]),
        "two matching display adapters rejected as ambiguous " + p.Id);
    Check(!M195WindowsGpuInventory.EligibleForRecoveryExport(
        DisplayFor(p, problem: 43)),
        "Code43 is not eligible for healthy recovery export " + p.Id);
    Check(!M195WindowsGpuInventory.EligibleForRecoveryExport(
        DisplayFor(p, inf: "display.inf")),
        "non-OEM active INF not eligible for recovery export " + p.Id);
    Check(!M195WindowsGpuInventory.EligibleForRecoveryExport(
        DisplayFor(p, service: "BasicDisplay", provider: "Microsoft")),
        "Microsoft BasicDisplay not eligible for recovery export " + p.Id);
}

string frozenGo1Revisionless = HandheldProfiles.Go1.HardwareId[
    ..HandheldProfiles.Go1.HardwareId.IndexOf("&REV_", StringComparison.Ordinal)];
var revisionlessGo1 = DisplayFor(HandheldProfiles.Go1) with {
    HardwareIds = [frozenGo1Revisionless]
};
Check(M195WindowsGpuInventory.RequireUniqueExactGpu([revisionlessGo1]).Profile ==
      HandheldProfiles.Go1,
      "frozen M194 Go1 revisionless PCI HardwareIds fallback preserved");
Reject(() => M195WindowsGpuInventory.RequireUniqueExactGpu(
        [revisionlessGo1 with {
            HardwareIds = [frozenGo1Revisionless,
                frozenGo1Revisionless + "&REV_05"]
        }]),
    "Go1 revisionless fallback rejects conflicting explicit wrong revision");
Reject(() => M195WindowsGpuInventory.RequireUniqueExactGpu(
        [DisplayFor(HandheldProfiles.Go2) with {
            HardwareIds = [HandheldProfiles.Go2.HardwareId[
                ..HandheldProfiles.Go2.HardwareId.IndexOf("&REV_", StringComparison.Ordinal)]]
        }]),
    "Go2 does not inherit Go1 revisionless hardware fallback");
Reject(() => M195WindowsGpuInventory.RequireUniqueExactGpu(
        [DisplayFor(HandheldProfiles.GoS) with {
            HardwareIds = [HandheldProfiles.GoS.HardwareId[
                ..HandheldProfiles.GoS.HardwareId.IndexOf("&REV_", StringComparison.Ordinal)]]
        }]),
    "Go S does not inherit Go1 revisionless hardware fallback");

Reject(() => M195WindowsGpuInventory.RequireUniqueExactGpu([]),
    "reject no supported display adapters");
Reject(() => M195WindowsGpuInventory.RequireUniqueExactGpu(
    [DisplayFor(HandheldProfiles.Go1) with {
        HardwareIds = [HandheldProfiles.Go1.HardwareId.Replace("REV_04", "REV_05",
            StringComparison.Ordinal)] }]),
    "reject near-match incorrect hardware revision");
Reject(() => M195WindowsGpuInventory.RequireUniqueExactGpu(
    [DisplayFor(HandheldProfiles.Go2) with {
        ClassGuid = Guid.NewGuid() }]),
    "reject hardware match in a non-Display device class");

if (OperatingSystem.IsWindows())
{
    IReadOnlyList<M195WindowsGpuInventory.ObservedDisplay> actualDisplays =
        await M195WindowsGpuInventory.EnumerateAsync();
    Check(actualDisplays.All(d => d.ClassGuid == M195WindowsGpuInventory.DisplayClass) &&
          actualDisplays.All(d => !string.IsNullOrWhiteSpace(d.InstanceId)),
          "native SetupAPI enumerates present Display class devices without mutation");
    Console.WriteLine("M195_NATIVE_DISPLAY_COUNT=" + actualDisplays.Count);
}



// Real M195 Windows preparation adapter MUST NOT silently implement the
// missing native signing/binding/MSI/reboot stage. No OS mutation in this test.
var realPrepOnly = new M195WindowsPreparationActions(
    new M195WindowsPreparationActions.Settings(
        "UNCREATED_RECOVERY_FOLDER", "UNCREATED_AMD_DOWNLOAD",
        "UNCREATED_SOURCE_EXTRACTION", "UNCREATED_UNSIGNED_STAGE",
        "UNAVAILABLE_PINNED_7ZR.EXE", "UNAVAILABLE_PINNED_INF2CAT.EXE",
        publishedInventoryText,
        "HKR,,OfflineDummyPolicy,0x00010001,0", null));

bool signBlocked = false, softwareBlocked = false, rebootBlocked = false;
try
{
    await realPrepOnly.SignOnceStageBindAndVerifyDualTrustAsync(
        null!, null!, null!, null!, CancellationToken.None);
}
catch (NotSupportedException ex)
{
    signBlocked = ex.Message.Contains("NOT_YET_PORTED", StringComparison.Ordinal);
}
try
{
    await realPrepOnly.InstallVerifiedAmdSoftwareAsync(null!, null!, CancellationToken.None);
}
catch (NotSupportedException ex)
{
    softwareBlocked = ex.Message.Contains("NOT_YET_PORTED", StringComparison.Ordinal);
}
try
{
    await realPrepOnly.RequestSingleRestartAsync(null!, CancellationToken.None);
}
catch (NotSupportedException ex)
{
    rebootBlocked = ex.Message.Contains("NOT_YET_PORTED", StringComparison.Ordinal);
}
Check(signBlocked && softwareBlocked && rebootBlocked,
    "real Windows Steps 1-7 adapter refuses unported live sign/bind/MSI/reboot operations");
if (OperatingSystem.IsWindows())
{
    string neverCreated = Path.Combine(Path.GetTempPath(),
        "Finesse-M195-NeverCreated-" + Guid.NewGuid().ToString("N"));
    var unhealthy = new M195WindowsGpuInventory.ExactGpu(
        DisplayFor(HandheldProfiles.Go2, problem:43), HandheldProfiles.Go2);
    Reject(() => M195WindowsRecoveryBaseline.ExportAsync(
        unhealthy, neverCreated).GetAwaiter().GetResult(),
        "real Windows pnputil export refuses unhealthy Go2 before starting a process");
    Check(!Directory.Exists(neverCreated),
        "rejected native baseline export creates no recovery directory");
}


// Frozen M179 read-only prebind identity selection, without invoking native
// stage/bind APIs. Exact profile mapping, real HardwareIds and published INF.
foreach (HandheldProfile p in new[] {
    HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2
})
{
    var actual = M195WindowsGpuInventory.RequireUniqueExactGpu([DisplayFor(p)]);
    string declared = "\"%TEST%\" = " + p.InstallSection + ", " + p.HardwareId + "\r\n";
    M195WindowsBindIdentity.ProposedBind proposed =
        M195WindowsBindIdentity.InspectProposedBind(actual, declared);
    Check(proposed.Profile == p &&
          proposed.HardwareId.Equals(p.HardwareId, StringComparison.OrdinalIgnoreCase) &&
          proposed.InstanceId == actual.Display.InstanceId,
        "M179 prebind selects observed exact HardwareId for " + p.Id);
    Reject(() => M195WindowsBindIdentity.InspectProposedBind(
        actual, declared + declared),
        "M179 prebind rejects duplicate target manufacturer mapping " + p.Id);
    Reject(() => M195WindowsBindIdentity.InspectProposedBind(
        actual, declared.Replace(p.HardwareId,
            p.HardwareId.Replace("REV_", "REV_0", StringComparison.Ordinal),
            StringComparison.Ordinal)),
        "M179 prebind rejects incorrect hardware mapping " + p.Id);
}
Check(M195WindowsBindIdentity.RequirePublishedOemInf("OEM45.INF") == "oem45.inf",
    "native stage published INF normalized only after exact OEM filename validation");
Reject(() => M195WindowsBindIdentity.RequirePublishedOemInf(@"C:\Windows\INF\oem45.inf"),
    "native stage published INF disallows full user-controlled path");
Reject(() => M195WindowsBindIdentity.RequirePublishedOemInf("display.inf"),
    "native stage published INF disallows arbitrary INF name");



// Actual Windows Step 9 classifier is read-only and reusable from the future
// live binder. Exact target INF/version + Code43 is provisional only.
foreach (HandheldProfile p in new[] {
    HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2
})
{
    const string driverVersion = "32.0.32015.16001";
    var actual = DisplayFor(p, inf: "oem49.inf", problem: 43) with {
        Version = driverVersion
    };
    var device = new L.Device(actual.InstanceId, p.HardwareId, p);
    var bound = new L.BoundDriver("oem49.inf", driverVersion,
        new string('B', 64), 14, true, true, true);
    var preparedGpu = new M195WindowsGpuInventory.ExactGpu(actual, p);
    L.PrebootObservation provisional =
        M195WindowsPrebootObservation.Classify(device, bound, preparedGpu);
    Check(provisional.Classification == L.PrebootState.Code43AwaitingPostboot,
        "native Step9 Code43 pending real postboot, not target success " + p.Id);
    L.PrebootObservation healthy = M195WindowsPrebootObservation.Classify(
        device, bound, preparedGpu with {
            Display = actual with { ProblemCode = 0 }
        });
    Check(healthy.Classification == L.PrebootState.Code0AwaitingPostboot,
        "native Step9 exact driver Code0 before reboot " + p.Id);
    L.PrebootObservation wrongInf = M195WindowsPrebootObservation.Classify(
        device, bound, preparedGpu with {
            Display = actual with { PublishedInf = "oem41.inf" }
        });
    Check(wrongInf.Classification == L.PrebootState.Reject,
        "native Step9 rejects wrong staged INF " + p.Id);
    L.PrebootObservation wrongVersion = M195WindowsPrebootObservation.Classify(
        device, bound, preparedGpu with {
            Display = actual with { Version = "32.0.32015.7029" }
        });
    Check(wrongVersion.Classification == L.PrebootState.Reject,
        "native Step9 rejects wrong GPU driver version " + p.Id);
    Reject(() => M195WindowsPrebootObservation.Classify(
        device, bound, preparedGpu with {
            Display = actual with { InstanceId = "PCI\\DIFFERENT_DEVICE_INSTANCE" }
        }),
        "native Step9 rejects changed GPU device instance " + p.Id);
}


// M195 retained M179 signer identity / READ-ONLY preflight tests.
// These do not create a certificate, run SignTool, or stage a GPU driver.
Check(M195LiveSigningPreflight.PinnedMicrosoftSignToolSha256 ==
      "80972965E7FC311D293222B1A0E2C1BFB60F363239173964DBE2A71638314B9F" &&
      M195LiveSigningPreflight.PinnedOriginalAmdMainCatalogSha256 ==
      "786D0B7F596D367F2C71BADAA31068DD1A63C20E0C8C7DE94E8E23019EBAFE04",
      "M179 verified original SignTool and authentic AMD catalog hashes preserved");
Check(M195LiveSigningPreflight.IsOwnedSigner(
          new string('A', 40),
          "CN=Finesse-Local-Live-0123456789abcdef0123456789abcdef"),
      "recognize the exact M179-owned LocalMachine signing certificate identity");
Check(!M195LiveSigningPreflight.IsOwnedSigner(
          new string('A', 40), "CN=Microsoft Windows") &&
      !M195LiveSigningPreflight.IsOwnedSigner(
          "not-a-sha1-thumbprint",
          "CN=Finesse-Local-Live-0123456789abcdef0123456789abcdef") &&
      !M195LiveSigningPreflight.IsOwnedSigner(
          new string('A', 40), "CN=Finesse-Local-Live-arbitrary"),
      "reject non-owned certificate subject or malformed thumbprint");
if (OperatingSystem.IsWindows())
{
    var fakeUnsigned = new Amd26111UnsignedCatalogGenerator.Result(
        "UNCREATED_UNSIGNED_MAIN_CATALOG.CAT", new string('C', 64),
        194, false, "NO_STDOUT", "NO_STDERR", 0);
    var fakeStage = new AmdSourceFirstStager.UnsignedStageResult(
        "UNCREATED_FAKE_PREPARED_ROOT", "UNCREATED_ORIGINAL_AMD_CAT",
        M195LiveSigningPreflight.PinnedOriginalAmdMainCatalogSha256,
        Amd26111SourceInventory.PinnedOriginalManifestSha256,
        Amd26111SourceInventory.PinnedCandidateManifestHash(HandheldProfiles.Go1),
        194, "UNCREATED_EVIDENCE", false);
    var fakeCandidate = new Amd26111CandidateRecipe.VerifiedCandidate(
        HandheldProfiles.Go1, [], [],
        "5F49F240CBDA41F7BD6E30867751507ACF38386B7AA3E0848639BCD002FE691A",
        "7986B5DC0CB3C2207ECE857D0060475A603B5342649D146874850C0D4A2509CA",
        Amd26111CandidateRecipe.ReleaseMarker);
    Reject(() => M195LiveSigningPreflight.Validate(
        fakeStage, fakeCandidate, fakeUnsigned,
        publishedInventoryText, "UNVERIFIED-SIGNTOOL.EXE"),
        "live signer refuses missing or unverified original source BEFORE machine trust mutation");
    Reject(() => M195LiveSigningPreflight.Validate(
        fakeStage with { VerifiedFiles = 193 }, fakeCandidate, fakeUnsigned,
        publishedInventoryText, "UNVERIFIED-SIGNTOOL.EXE"),
        "live signer refuses 193-file candidate, never silently skips catalog members");
    Reject(() => M195LiveSigningPreflight.Validate(
        fakeStage, fakeCandidate,
        fakeUnsigned with { CatalogContainsSigners = true },
        publishedInventoryText, "UNVERIFIED-SIGNTOOL.EXE"),
        "live signer refuses a previously signed or reused catalog");
}


// Parse-only test of the actual PowerShell 5.1 script embedded in the M179
// live-signing port. Never executes New-SelfSignedCertificate or SignTool.
if (OperatingSystem.IsWindows())
{
    var signerType = typeof(M195LiveSigningPreflight).Assembly.GetType(
        "Finesse.Compatibility.M195LiveCatalogSigner", throwOnError: true)!;
    var scriptConstant = signerType.GetField("Script",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
    string embeddedScript = scriptConstant?.GetRawConstantValue() as string ??
        throw new InvalidOperationException("Live signer PowerShell source missing.");
    string parseRoot = Path.Combine(Path.GetTempPath(),
        "Finesse-M195-SignerParse-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(parseRoot);
    try
    {
        string signerScript = Path.Combine(parseRoot, "read-only-signer-source.ps1");
        File.WriteAllText(signerScript, embeddedScript);
        string parser = Path.Combine(parseRoot, "parse-only.ps1");
        File.WriteAllText(parser,
            "$ErrorActionPreference='Stop'\r\n" +
            "$tokens=$null; $parseErrors=$null\r\n" +
            "[void][System.Management.Automation.Language.Parser]::ParseFile(" +
            "$env:FINESSE_M195_SCRIPT_PARSE_TARGET,[ref]$tokens,[ref]$parseErrors)\r\n" +
            "if(@($parseErrors).Count -ne 0){" +
            "foreach($entry in @($parseErrors)){Write-Output $entry.ToString()}; exit 1}\r\n" +
            "Write-Output 'M195_PS51_SCRIPT_PARSE_PASS'\r\n");
        var psi = new System.Diagnostics.ProcessStartInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["FINESSE_M195_SCRIPT_PARSE_TARGET"] = signerScript;
        foreach (string argument in new[] {
            "-NoLogo", "-NoProfile", "-NonInteractive",
            "-File", parser })
            psi.ArgumentList.Add(argument);
        using var parseProcess = System.Diagnostics.Process.Start(psi) ??
            throw new InvalidOperationException("Unable to start Windows PowerShell parser.");
        string parseStdout = await parseProcess.StandardOutput.ReadToEndAsync();
        string parseStderr = await parseProcess.StandardError.ReadToEndAsync();
        if (!parseProcess.WaitForExit(20_000))
        {
            parseProcess.Kill(entireProcessTree: true);
            throw new InvalidOperationException("PowerShell parser timed out.");
        }
        Check(parseProcess.ExitCode == 0 &&
              parseStdout.Contains("M195_PS51_SCRIPT_PARSE_PASS", StringComparison.Ordinal),
              "M179-derived retained installation signer parses in real Windows PowerShell 5.1");
        Check(embeddedScript.Contains("LocalMachine", StringComparison.Ordinal) &&
              embeddedScript.Contains("New-SelfSignedCertificate", StringComparison.Ordinal) &&
              embeddedScript.Contains("TrustedPublisher", StringComparison.Ordinal) &&
              embeddedScript.Contains("M195-SignTool-VerifyINF.txt", StringComparison.Ordinal),
              "retained signer includes source-derived trust, SignTool and INF membership evidence");
    }
    finally
    {
        Directory.Delete(parseRoot, recursive: true);
    }
}


// Exact 14 critical graphics file SHA-256 targets recovered from frozen M191.
// Metadata-only regression: no AMD binary files are checked into the repo.
M195AmdDualTrustTargets.VerifySourceInventory(frozenOriginal);
Check(M195AmdDualTrustTargets.For26111.Count == 14 &&
      M195AmdDualTrustTargets.For26111.Select(t => t.Relative)
          .Distinct(StringComparer.OrdinalIgnoreCase).Count() == 14,
      "frozen M191 original AMD dual-catalog critical target contract is 14 unique files");
Check(M195AmdDualTrustTargets.For26111.Any(t =>
        t.Relative.EndsWith("/amdxc64.dll", StringComparison.OrdinalIgnoreCase) &&
        t.OriginalSha256 ==
            "5090826B251CD75CFB4F8583EE744089382B5C88CE62D78A46128AED0B8CF33B") &&
      M195AmdDualTrustTargets.For26111.Any(t =>
        t.Relative.EndsWith("/amdkmdag.sys", StringComparison.OrdinalIgnoreCase) &&
        t.OriginalSha256 ==
            OfficialAmd26111SourceContract.VerifiedFrozenM194.OriginalKernelSha256),
      "kernel and AMDXC critical graphics hashes are preserved from authenticated original");


// Ported frozen M115 native Windows CryptCATAdmin v2c logic: read-only
// lookup against a runner system file, never registration or machine mutation.
if (OperatingSystem.IsWindows())
{
    string signedCandidate = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");
    Check(File.Exists(signedCandidate),
        "native CryptCATAdmin test has a Windows system file");
    IReadOnlyList<string> registered =
        M195WindowsCatalogRegistry.EnumerateForFile(signedCandidate);
    Check(registered.All(File.Exists),
        "native CryptCATAdmin successfully enumerates readable catalog paths without mutation");
    Console.WriteLine("M195_NATIVE_CATALOG_REGISTRATIONS=" + registered.Count);
    Reject(() => M195WindowsCatalogRegistry.ObserveDualMembership(
        signedCandidate, new string('0', 64),
        M195LiveSigningPreflight.PinnedOriginalAmdMainCatalogSha256,
        new string('1', 64)),
        "native catalog membership rejects mismatched content hash BEFORE enumeration");
}


// M194 M191 full dual-catalog evidence: 14 exact AMD bytes, original + local
// SignTool membership, and original + local Windows registered membership.
var dualVerifiedMembers = M195AmdDualTrustTargets.For26111.Select(t =>
    new M195DualCatalogCoverage.FileEvidence(t.Relative, t.OriginalSha256,
        true, true, true, true)).ToArray();
M195DualCatalogCoverage.Snapshot FullDualSnapshot(
    IReadOnlyList<M195DualCatalogCoverage.FileEvidence> members) =>
    new(true, true, true, true, true, members);
var allDual = M195DualCatalogCoverage.Evaluate(
    FullDualSnapshot(dualVerifiedMembers));
Check(allDual.DualTrustVerified && allDual.OriginalCoverage == 14 &&
      allDual.LocalCoverage == 14 && allDual.RegisteredOriginalCoverage == 14 &&
      allDual.RegisteredLocalCoverage == 14 &&
      !allDual.OriginalCatalogRegistrationSuggested &&
      allDual.MissingEvidence.Count == 0,
    "M191 four independent 14/14 coverage proofs produce full dual trust");

var missingOriginalRegistration = dualVerifiedMembers.Select(
    m => m with { RegisteredOriginalCatalogFound = false }).ToArray();
var incompleteDual = M195DualCatalogCoverage.Evaluate(
    FullDualSnapshot(missingOriginalRegistration));
Check(!incompleteDual.DualTrustVerified &&
      incompleteDual.OriginalCoverage == 14 &&
      incompleteDual.RegisteredOriginalCoverage == 0 &&
      incompleteDual.OriginalCatalogRegistrationSuggested &&
      incompleteDual.MissingEvidence.Count == 14,
    "M191 missing original registered catalog must not claim dual trust, but permits targeted remediation");

var onlyCatalogFilesPresent = M195DualCatalogCoverage.Evaluate(
    FullDualSnapshot(dualVerifiedMembers.Select(m => m with {
        OriginalCatalogMemberVerified = false,
        LocalCatalogMemberVerified = false }).ToArray()));
Check(!onlyCatalogFilesPresent.DualTrustVerified &&
      !onlyCatalogFilesPresent.OriginalCatalogRegistrationSuggested,
    "catalog registration without independently verified 14 file memberships is not dual trust");

var badKernelSignature = M195DualCatalogCoverage.Evaluate(
    new M195DualCatalogCoverage.Snapshot(true, true, true, false,
        true, dualVerifiedMembers));
Check(!badKernelSignature.DualTrustVerified &&
      !badKernelSignature.OriginalCatalogRegistrationSuggested,
    "original AMD CAT without kernel-signature policy cannot authorize registration");

var wrongDriverStoreCatalog = M195DualCatalogCoverage.Evaluate(
    new M195DualCatalogCoverage.Snapshot(true, false, true, true,
        true, dualVerifiedMembers));
Check(!wrongDriverStoreCatalog.DualTrustVerified &&
      !wrongDriverStoreCatalog.OriginalCatalogRegistrationSuggested,
    "catalog on disk is insufficient if published Driver Store CAT differs");

Reject(() => M195DualCatalogCoverage.Evaluate(FullDualSnapshot(
    dualVerifiedMembers[..13])),
    "refuse incomplete 13/14 driver critical-file evidence");
Reject(() => M195DualCatalogCoverage.Evaluate(FullDualSnapshot(
    dualVerifiedMembers.Select((m, i) => i == 0
        ? m with { Sha256 = new string('0', 64) } : m).ToArray())),
    "refuse expected AMD binary modified even if all catalog booleans true");
Reject(() => M195DualCatalogCoverage.Evaluate(FullDualSnapshot(
    dualVerifiedMembers.Select((m, i) => i == 0
        ? dualVerifiedMembers[1] : m).ToArray())),
    "refuse duplicate critical-file evidence masquerading as 14/14");


// Actual M191 SetupAPI driver-store mapping. This is read-only; unlike a
// same-name local package, a published OEM INF must resolve via Windows.
if (OperatingSystem.IsWindows())
{
    string infDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF");
    string[] oemInfs = Directory.GetFiles(infDirectory, "oem*.inf",
        SearchOption.TopDirectoryOnly).Where(p =>
        System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),
            @"\Aoem[0-9]+\.inf\z", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        .ToArray();
    if (oemInfs.Length > 0)
    {
        var locatedDriver = M195WindowsDriverStoreLocator.Locate(
            Path.GetFileName(oemInfs[0]));
        Check(File.Exists(locatedDriver.DriverStoreInfPath) &&
              locatedDriver.DriverStoreInfPath.Contains(
                  "FileRepository", StringComparison.OrdinalIgnoreCase) &&
              locatedDriver.InfSha256.Length == 64,
              "native SetupGetInfDriverStoreLocation resolves an actually published OEM INF");
        Console.WriteLine("M195_DRIVERSTORE_MAPPED_OEM=" + locatedDriver.PublishedInf);
    }
    else
        Console.WriteLine("M195_DRIVERSTORE_POSITIVE_MAPPING_SKIPPED_NO_OEM_INF_ON_RUNNER");
    Reject(() => M195WindowsDriverStoreLocator.Locate("not-an-oem.inf"),
        "driverstore locator refuses invalid published INF before invoking native SetupAPI");
}


// Exact live bound GPU precondition for native M191 dual-catalog observation.
// A staged OEM INF is not an assigned GPU driver. Code43 pending reboot is
// permitted for observation but still never counts as postboot Code0.
foreach (HandheldProfile testProfile in new[] {
    HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2
})
{
    string version = OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion;
    var observed = DisplayFor(testProfile, instance: "PCI\\M195_TEST_GPU",
        inf: "oem88.inf", problem: 43) with { Version = version };
    var gpu = new M195WindowsGpuInventory.ExactGpu(observed, testProfile);
    var expectedDevice = new L.Device(observed.InstanceId, testProfile.HardwareId, testProfile);
    M195WindowsDualCatalogAudit.RequireAssignedTargetDriver(
        expectedDevice, "oem88.inf", gpu);
    Check(true, "native M191 dual-catalog accepts exact preboot assigned GPU Code43 " + testProfile.Id);
    Reject(() => M195WindowsDualCatalogAudit.RequireAssignedTargetDriver(
        expectedDevice, "oem77.inf", gpu),
        "reject staged but NOT assigned OEM INF for M191 audit " + testProfile.Id);
    Reject(() => M195WindowsDualCatalogAudit.RequireAssignedTargetDriver(
        expectedDevice, "oem88.inf",
        gpu with { Display = observed with { Version = "32.0.32015.7029" } }),
        "reject unrelated active GPU driver version " + testProfile.Id);
    Reject(() => M195WindowsDualCatalogAudit.RequireAssignedTargetDriver(
        expectedDevice with { InstanceId = "PCI\\OTHER_GPU" }, "oem88.inf", gpu),
        "reject different assigned GPU instance " + testProfile.Id);
}


// Frozen M179 NewDev bind migration: policy tests are PURE and do not call
// UpdateDriverForPlugAndPlayDevicesW on GitHub-hosted Windows.
foreach (HandheldProfile profile in new[] {
    HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2
})
{
    var display = DisplayFor(profile, instance: "PCI\\M195_BIND_GPU");
    var gpu = new M195WindowsGpuInventory.ExactGpu(display, profile);
    var device = new L.Device(display.InstanceId, profile.HardwareId, profile);
    var baselineAssigned = new L.AssignedDriver(display.PublishedInf, display.Version,
        0, true, "healthy source baseline");
    string infText = "\"AMD Radeon\" = " + profile.InstallSection + ", " +
        profile.HardwareId + "\r\n";
    var plan = M195WindowsNativeBind.RequirePlan(device, baselineAssigned, gpu,
        "oem91.inf", profile.HardwareId, device.InstanceId, infText);
    Check(plan.Profile == profile && plan.HardwareId == profile.HardwareId &&
          plan.StagedPublishedInf == "oem91.inf" &&
          plan.OriginalPublishedInf == "oem42.inf",
        "native NewDev plan selects the exact hardware profile and staged published INF " + profile.Id);
    Reject(() => M195WindowsNativeBind.RequirePlan(
        device, baselineAssigned, gpu, "oem91.inf",
        profile.HardwareId + "&BAD", device.InstanceId, infText),
        "native NewDev plan rejects modified stage hardware ID " + profile.Id);
    Reject(() => M195WindowsNativeBind.RequirePlan(
        device, baselineAssigned, gpu, "oem91.inf",
        profile.HardwareId, "PCI\\WRONG_INSTANCE", infText),
        "native NewDev plan rejects changed GPU instance " + profile.Id);
    Reject(() => M195WindowsNativeBind.RequirePlan(
        device, baselineAssigned, gpu with { Display = display with { PublishedInf = "oem3.inf" } },
        "oem91.inf", profile.HardwareId, device.InstanceId, infText),
        "native NewDev plan rejects original active INF drift " + profile.Id);
    Reject(() => M195WindowsNativeBind.RequirePlan(
        device, baselineAssigned, gpu with { Display = display with { Version = "different" } },
        "oem91.inf", profile.HardwareId, device.InstanceId, infText),
        "native NewDev plan rejects original active version drift " + profile.Id);
    Reject(() => M195WindowsNativeBind.RequirePlan(
        device, baselineAssigned, gpu, "../oem91.inf", profile.HardwareId,
        device.InstanceId, infText),
        "native NewDev plan rejects invalid published INF path " + profile.Id);
    Reject(() => M195WindowsNativeBind.RequirePlan(
        device, baselineAssigned, gpu, "oem91.inf", profile.HardwareId,
        device.InstanceId, infText.Replace(profile.InstallSection, "wrong_section")),
        "native NewDev plan rejects wrong INF install section " + profile.Id);

    Check(M195WindowsNativeBind.ClassifyRecovery(
        true, false, plan, gpu) == "RECOVERY_VERIFIED_CODE0_BASELINE",
        "M180 rollback requires original active INF/version/device and Code0 " + profile.Id);
    Check(M195WindowsNativeBind.ClassifyRecovery(
        true, true, plan, gpu) == "RECOVERY_PENDING_RESTART_UNVERIFIED",
        "M180 rollback Code0 with pending reboot remains UNVERIFIED " + profile.Id);
    Check(M195WindowsNativeBind.ClassifyRecovery(
        false, false, plan, gpu) == "RECOVERY_BIND_API_FAILED",
        "M180 rollback failed API cannot be claimed healthy " + profile.Id);
    Check(M195WindowsNativeBind.ClassifyRecovery(
        true, false, plan, null) == "RECOVERY_DEVICE_NOT_OBSERVED",
        "M180 rollback missing GPU observation is not healthy " + profile.Id);
    Check(M195WindowsNativeBind.ClassifyRecovery(
        true, false, plan,
        gpu with { Display = display with { ProblemCode = 43 } }) ==
            "RECOVERY_DEVICE_PROBLEM",
        "M180 rollback Code43 is never a recovered Code0 baseline " + profile.Id);
    Check(M195WindowsNativeBind.ClassifyRecovery(
        true, false, plan,
        gpu with { Display = display with { Version = "0.0.0.0" } }) ==
            "RECOVERY_BASELINE_MISMATCH",
        "M180 rollback wrong driver version is not recovery " + profile.Id);
    Check(M195WindowsNativeBind.ClassifyRecovery(
        true, false, plan,
        gpu with { Display = display with { InstanceId = "PCI\\DRIFT" } }) ==
            "RECOVERY_DEVICE_ID_MISMATCH",
        "M180 rollback altered GPU instance is not recovery " + profile.Id);
}


// M195 production Step8 ordering: fake phases only. These fixtures never
// sign, stage or bind drivers on the Windows CI runner.
foreach (HandheldProfile testedProfile in new[] {
    HandheldProfiles.Go1, HandheldProfiles.GoS, HandheldProfiles.Go2
})
{
    var step8Context = new M195Step8Sequence.Context(
        new L.Device("PCI\\STEP8_TEST", testedProfile.HardwareId, testedProfile),
        new L.AssignedDriver("oem42.inf", "32.0.32015.7029", 0,
            true, "healthy"),
        new L.Baseline("FAKE_VERIFIED_EXPORT", true, "oem42.inf"));
    var step8Fake = new FakeStep8Phases();
    var step8Result = await M195Step8Sequence.ExecuteAsync(step8Context, step8Fake);
    Check(step8Fake.Calls.SequenceEqual(new[] {
            "Sign", "Stage", "Bind", "Audit" }) &&
          step8Result.Bound.DualTrustedCriticalFiles == 14 &&
          step8Result.Bound.SignedOnce &&
          step8Result.Trust.FullDualTrustVerified,
        "Step8 fake orders sign-stage-bind-audit and returns measured 14x4 " + testedProfile.Id);

    var step8Partial = new FakeStep8Phases { AuditIncomplete = true };
    var partialResult = await M195Step8Sequence.ExecuteAsync(step8Context, step8Partial);
    Check(partialResult.Bound.DualTrustedCriticalFiles == 0 &&
          !partialResult.Trust.FullDualTrustVerified &&
          partialResult.Trust.Warning is not null &&
          !step8Partial.Calls.Contains("Recover"),
        "incomplete optional M191 dual-trust evidence is disclosed but not blanket blocker " +
        testedProfile.Id);

    var step8AuditFailure = new FakeStep8Phases { AuditThrowsCritical = true };
    Reject(() => M195Step8Sequence.ExecuteAsync(
        step8Context, step8AuditFailure).GetAwaiter().GetResult(),
        "critical postbind AMD catalog/source corruption triggers failure " + testedProfile.Id);
    Check(step8AuditFailure.Calls.SequenceEqual(new[] {
            "Sign", "Stage", "Bind", "Audit", "Recover" }),
        "critical postbind audit failure attempts observed baseline recovery " + testedProfile.Id);

    var step8InvalidStage = new FakeStep8Phases { StageInvalid = true };
    Reject(() => M195Step8Sequence.ExecuteAsync(
        step8Context, step8InvalidStage).GetAwaiter().GetResult(),
        "invalid SetupAPI stage receipt refused before driver bind " + testedProfile.Id);
    Check(!step8InvalidStage.Calls.Contains("Bind") &&
          !step8InvalidStage.Calls.Contains("Recover"),
        "failed staging cannot trigger a fake postbind rollback " + testedProfile.Id);

    var step8BadSigner = new FakeStep8Phases { SignInvalid = true };
    Reject(() => M195Step8Sequence.ExecuteAsync(
        step8Context, step8BadSigner).GetAwaiter().GetResult(),
        "signer must retain trust and evidence before stage " + testedProfile.Id);
    Check(step8BadSigner.Calls.SequenceEqual(new[] { "Sign" }),
        "signing refusal cannot proceed to stage or bind " + testedProfile.Id);

    var step8WrongAssignment = new FakeStep8Phases { WrongBoundVersion = true };
    Reject(() => M195Step8Sequence.ExecuteAsync(
        step8Context, step8WrongAssignment).GetAwaiter().GetResult(),
        "wrong assigned active driver version rejected after NewDev " + testedProfile.Id);
    Check(step8WrongAssignment.Calls.Contains("Recover") &&
          !step8WrongAssignment.Calls.Contains("Audit"),
        "wrong active binding recovered before optional dual-trust audit " + testedProfile.Id);
}
var step8Go1BrokenBaseline = new M195Step8Sequence.Context(
    new L.Device("PCI\\GO1_BROKEN", HandheldProfiles.Go1.HardwareId, HandheldProfiles.Go1),
    new L.AssignedDriver("oem2.inf", "32.0.23044.19", 43,
        false, "broken initial GPU"),
    new L.Baseline(null, false, null));
var optionalGo1Result = await M195Step8Sequence.ExecuteAsync(
    step8Go1BrokenBaseline, new FakeStep8Phases());
Check(optionalGo1Result.VerifiedPrebootAssignment,
    "frozen Go1 path retains optional recovery export for broken initial GPU");
var go2WithoutBackup = new M195Step8Sequence.Context(
    new L.Device("PCI\\GO2_BROKEN", HandheldProfiles.Go2.HardwareId, HandheldProfiles.Go2),
    new L.AssignedDriver("oem2.inf", "32.0.23044.19", 43,
        false, "broken initial GPU"),
    new L.Baseline(null, false, null));
var missingBackupFake = new FakeStep8Phases();
Reject(() => M195Step8Sequence.ExecuteAsync(
    go2WithoutBackup, missingBackupFake).GetAwaiter().GetResult(),
    "Go2 tester must retain frozen verified recovery baseline requirement");
Check(missingBackupFake.Calls.Count == 0,
    "Go2 missing backup prevents signing, staging or any mutation");


// M169 exact MSI identity and M184 log-authorized fallback. These synthetic
// checks NEVER execute msiexec or remove a registered desktop AMD product.
Check(M195AmdSettings26111.MsiSha256 ==
      "B1AFF2C1494877E15E23025D6B09F898E8E422777DA00CA013695181D7F116F2" &&
      M195AmdSettings26111.CccBytes == 246_780_968 &&
      M195AmdSettings26111.MsiBytes == 155_561_984 &&
      M195AmdSettings26111.ProductCode ==
        "{9E860308-0949-4D1D-BABD-980EEC7CE8CD}" &&
      M195AmdSettings26111.UpgradeCode ==
        "{A837E2B7-5BA6-4286-BC21-A002FCDD9A85}",
    "M169 frozen AMD 26.11.1 CCC2/MSI/UpgradeCode pins match original contract");
string wixLog = "Action start: FindRelatedProducts.\r\n" +
    "Property(S): WIX_DOWNGRADE_DETECTED = {A8E5B545-3B0F-4A84-B766-EF079C00EAF9}\r\n" +
    "Action ended 12:10:00: LaunchConditions. Return value 3.\r\n";
string[] blockers = M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(1603, wixLog).ToArray();
Check(blockers.Length == 1 &&
      blockers[0] == "{A8E5B545-3B0F-4A84-B766-EF079C00EAF9}",
    "M184 original 1603 WIX_DOWNGRADE_DETECTED parses one precise product identity");
foreach (int code in new[] { 0, 3010, 1602, 1618, 1638, 5 })
    Check(M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(code, wixLog).Count == 0,
        "M184 non-1603 MSI code " + code + " cannot authorize removal");
Check(M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(1603,
        wixLog.Replace("FindRelatedProducts", "AnotherAction")).Count == 0,
    "M184 missing FindRelatedProducts prevents uninstall fallback");
Check(M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(1603,
        wixLog.Replace("Return value 3", "Return value 1")).Count == 0,
    "M184 successful LaunchConditions cannot authorize removal");
Check(M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(1603,
        wixLog.Replace("WIX_DOWNGRADE_DETECTED", "ANY_OTHER_PROPERTY")).Count == 0,
    "M184 unrelated MSI properties cannot authorize removal");
Check(M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(1603,
        wixLog + wixLog).Count == 0,
    "M184 multiple candidate blocker properties are ambiguous");
Check(M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(1603,
        wixLog.Replace("{A8E5B545-3B0F-4A84-B766-EF079C00EAF9}", "invalid")).Count == 0,
    "M184 malformed GUID cannot authorize uninstall");
Check(M195AmdSettings26111.ReadVerifiedDowngradeBlockerCodes(1603,
        wixLog.Replace("{A8E5B545-3B0F-4A84-B766-EF079C00EAF9}",
            "{A8E5B545-3B0F-4A84-B766-EF079C00EAF9};{A8E5B545-3B0F-4A84-B766-EF079C00EAF9}")).Count == 0,
    "M184 duplicate MSI blocker code rejected");
var nativeRelatedMsi = new M195AmdSettings26111.RegisteredProduct(
    blockers[0], "AMD Settings", "Advanced Micro Devices, Inc.",
    "2026.1015.1803.2112", true, true);
Check(M195AmdSettings26111.RequireVerifiedBlockers(
        blockers, [nativeRelatedMsi]).Count == 1,
    "M184 real blocker must be registered related AMD Settings with exact publisher");
Reject(() => M195AmdSettings26111.RequireVerifiedBlockers(
    blockers, [nativeRelatedMsi with { Publisher = "Other Publisher" }]),
    "M184 foreign vendor product cannot be uninstalled");
Reject(() => M195AmdSettings26111.RequireVerifiedBlockers(
    blockers, [nativeRelatedMsi with { Name = "Other AMD Product" }]),
    "M184 other AMD product cannot be uninstalled");
Reject(() => M195AmdSettings26111.RequireVerifiedBlockers(
    blockers, [nativeRelatedMsi with { RelatedByExactUpgradeCode = false }]),
    "M184 wrong upgrade code cannot be uninstalled");
Reject(() => M195AmdSettings26111.RequireVerifiedBlockers(
    blockers, [nativeRelatedMsi with { Installed = false }]),
    "M184 uninstalled stale product cannot be removed again");
Reject(() => M195AmdSettings26111.RequireVerifiedBlockers(
    blockers, [nativeRelatedMsi with { Code = "{55555555-5555-4555-8555-555555555555}" }]),
    "M184 unrelated native MSI product identity cannot be removed");
Reject(() => M195AmdSettings26111.RequireVerifiedBlockers(
    blockers, [nativeRelatedMsi, nativeRelatedMsi]),
    "M184 duplicate related MSI inventory rejected");
Reject(() => M195AmdSettings26111.RequireVerifiedBlockers(
    [M195AmdSettings26111.ProductCode], [nativeRelatedMsi with {
        Code = M195AmdSettings26111.ProductCode }]),
    "M184 MSI target itself cannot be removed");
Check(M195AmdSettings26111.IsTargetMsiSuccess(0) &&
      M195AmdSettings26111.IsTargetMsiSuccess(3010) &&
      !M195AmdSettings26111.IsTargetMsiSuccess(1603),
    "M169 MSI return codes 0/3010 success while 1603 remains failure");


var pinnedSettings = new M195WindowsAmdSettingsMsiIdentity.PackageIdentity(
    M195AmdSettings26111.ProductCode, M195AmdSettings26111.UpgradeCode,
    M195AmdSettings26111.ProductName, M195AmdSettings26111.ProductVersion,
    "Advanced Micro Devices, Inc.");
M195WindowsAmdSettingsMsiIdentity.RequirePinned26111(pinnedSettings);
Check(true, "native MSI property-table matching accepts exact M169 AMD Settings identity");
Reject(() => M195WindowsAmdSettingsMsiIdentity.RequirePinned26111(
    pinnedSettings with { ProductCode = "{11111111-1111-4111-8111-111111111111}" }),
    "native MSI identity refuses wrong ProductCode");
Reject(() => M195WindowsAmdSettingsMsiIdentity.RequirePinned26111(
    pinnedSettings with { UpgradeCode = "{22222222-2222-4222-8222-222222222222}" }),
    "native MSI identity refuses wrong UpgradeCode");
Reject(() => M195WindowsAmdSettingsMsiIdentity.RequirePinned26111(
    pinnedSettings with { ProductName = "AMD Link" }),
    "native MSI identity refuses unrelated AMD program");
Reject(() => M195WindowsAmdSettingsMsiIdentity.RequirePinned26111(
    pinnedSettings with { ProductVersion = "2026.1015.1803.2112" }),
    "native MSI identity refuses older release's product version");
Reject(() => M195WindowsAmdSettingsMsiIdentity.RequirePinned26111(
    pinnedSettings with { Manufacturer = "Another Vendor" }),
    "native MSI identity refuses foreign publisher");


var exactMsiArgs = M195AmdSettings26111.BuildMsiexecArguments(
    true, @"C:\\AMD Settings\\ccc-next64.msi",
    @"C:\\Evidence\\first-install.log").ToArray();
Check(exactMsiArgs.SequenceEqual(new[] {
        "/i", @"C:\\AMD Settings\\ccc-next64.msi",
        "/qn", "/norestart", "/L*V", @"C:\\Evidence\\first-install.log" }),
    "M140 target MSI first-install command is silent, quoted via ArgumentList, no restart");
var exactUninstallArgs = M195AmdSettings26111.BuildMsiexecArguments(
    false, blockers[0], @"C:\\Evidence\\uninstall.log").ToArray();
Check(exactUninstallArgs.SequenceEqual(new[] {
        "/x", blockers[0], "/qn", "/norestart", "/L*V",
        @"C:\\Evidence\\uninstall.log" }),
    "M184 verified blocker-only uninstall command is silent with no restart");
Reject(() => M195AmdSettings26111.BuildMsiexecArguments(
    false, M195AmdSettings26111.ProductCode, "target-uninstall.log"),
    "MSI argument builder refuses removing the exact target ProductCode");
Reject(() => M195AmdSettings26111.BuildMsiexecArguments(
    false, "BAD-GUID", "bad-uninstall.log"),
    "MSI argument builder refuses product-code without exact B GUID format");
Reject(() => M195AmdSettings26111.BuildMsiexecArguments(
    true, "wrong-source.exe", "bad-source.log"),
    "MSI argument builder refuses execution of a full EXE as the AMD Settings MSI");
Reject(() => M195AmdSettings26111.BuildMsiexecArguments(
    true, "ccc-next64.msi", "no-verbose-extension"),
    "MSI argument builder requires a fresh verbose .log destination");


// Native Windows MSI ProductState / related upgrade family calls are READ-ONLY
// on a hosted Windows runner; no MSI package, uninstall, or restart is invoked.
if (OperatingSystem.IsWindows())
{
    IReadOnlyList<M195AmdSettings26111.RegisteredProduct> nativeRelatedAmd =
        M195WindowsAmdSettingsMsiIdentity.EnumerateRelatedDesktopProducts();
    Check(nativeRelatedAmd.Count < 100 &&
          nativeRelatedAmd.All(item => item.Installed &&
              item.RelatedByExactUpgradeCode &&
              Guid.TryParseExact(item.Code, "B", out _)),
        "Windows msi.dll enumerates only native installed related MSI product identities");
    bool exactTargetPresent =
        M195WindowsAmdSettingsMsiIdentity.ExactTargetInstalled();
    Console.WriteLine("M195_NATIVE_TARGET_AMD_SETTINGS_INSTALLED=" + exactTargetPresent);
    Check(true, "read-only native MsiQueryProductState called for exact 26.11.1 target");
}

Console.WriteLine($"OFFLINE_COMPATIBILITY_TESTS_PASS={passed}");


sealed class FakeM195Lifecycle : L.IWindowsInstallationActions, L.IPostbootActions
{
    public List<string> Calls { get; } = [];
    public bool FailSoftware { get; init; }
    public bool VerifiedBaseline { get; init; } = true;
    public bool UseGo2 { get; init; }
    public int DualTrustedCount { get; init; } = 14;
    public int PostbootProblemCode { get; init; }
    public string PostbootIdentity { get; init; } = "boot-B";
    private L.Device Device => new("PCI\\VEN_1002&FAKE_INSTANCE",
        (UseGo2 ? HandheldProfiles.Go2 : HandheldProfiles.Go1).HardwareId,
        UseGo2 ? HandheldProfiles.Go2 : HandheldProfiles.Go1);

    public Task<L.Device> IdentifyExactDeviceAsync(CancellationToken token)
    {
        Calls.Add("Identify");
        return Task.FromResult(Device);
    }
    public Task<L.AssignedDriver> InspectAssignedDriverAsync(L.Device device, CancellationToken token)
    {
        Calls.Add("Inspect");
        return Task.FromResult(new L.AssignedDriver("oem34.inf", "32.0.32015.7029", 0,
            true, "healthy active GPU"));
    }
    public Task<L.Baseline> ExportBaselineAsync(
        L.Device device, L.AssignedDriver assigned, CancellationToken token)
    {
        Calls.Add("Backup");
        return Task.FromResult(new L.Baseline(
            VerifiedBaseline ? "FAKE_BACKUP_DIR_NOT_CREATED" : null,
            VerifiedBaseline, VerifiedBaseline ? "oem34.inf" : null));
    }
    public Task<OfficialAmdInstallerVerifier.VerifiedSource> AcquireVerifiedAmdInstallerAsync(
        CancellationToken token)
    {
        Calls.Add("Acquire");
        var contract = OfficialAmd26111SourceContract.VerifiedFrozenM194;
        return Task.FromResult(new OfficialAmdInstallerVerifier.VerifiedSource(
            "FAKE_VERIFIED_AMD_SOURCE_NOT_ON_DISK", contract.InstallerSha256,
            contract.ExactInstallerBytes, "Advanced Micro Devices, Inc.",
            "FAKE_WINDOWS_SIGNER_THUMBPRINT", DateTimeOffset.UtcNow));
    }
    public Task<Amd26111ArchiveExtraction.VerifiedExtraction> ExtractOriginalAmdAsync(
        OfficialAmdInstallerVerifier.VerifiedSource source, CancellationToken token)
    {
        Calls.Add("Extract");
        return Task.FromResult(new Amd26111ArchiveExtraction.VerifiedExtraction(
            "FAKE_AMD_SOURCE", "FAKE_EXTRACTION", Amd26111SourceInventory.PinnedOriginalManifestSha256,
            new string('A', 64), 194, 1, "FAKE_EXTRACT_RECEIPT"));
    }
    public Task<Amd26111TwoPhasePreparation.PreparationReceipt> BuildUnsignedCandidateAsync(
        L.Device device, OfficialAmdInstallerVerifier.VerifiedSource source,
        Amd26111ArchiveExtraction.VerifiedExtraction extracted, CancellationToken token)
    {
        Calls.Add("Prepare");
        string inf = device.Profile.Id switch
        {
            "LegionGo1" => "5F49F240CBDA41F7BD6E30867751507ACF38386B7AA3E0848639BCD002FE691A",
            "LegionGo2-Z2Extreme" => "A355187F07FEEE5A0E0EDCC772A95C68E4F54B04323DE3D45634FFBE447CCBA3",
            _ => "E2B0809F3E3D135BFD03F1E0D97D1140BD6A05557024CE1D3EB604DFDAA90071"
        };
        string dat = device.Profile == HandheldProfiles.Go2
            ? "B63E4F58C164950B41C07071B3CF6797B35ACC597B9476836FCA66E8A658E3FD"
            : "7986B5DC0CB3C2207ECE857D0060475A603B5342649D146874850C0D4A2509CA";
        var candidate = new Amd26111CandidateRecipe.VerifiedCandidate(
            device.Profile, [], [], inf, dat, Amd26111CandidateRecipe.ReleaseMarker);
        var stage = new AmdSourceFirstStager.UnsignedStageResult(
            "FAKE_UNSIGNED_PREPARED_FOLDER", "FAKE_ORIGINAL_CAT", new string('A', 64),
            Amd26111SourceInventory.PinnedOriginalManifestSha256,
            Amd26111SourceInventory.PinnedCandidateManifestHash(device.Profile),
            194, "FAKE_STAGE_RECEIPT", false);
        return Task.FromResult(new Amd26111TwoPhasePreparation.PreparationReceipt(
            source, candidate, stage));
    }
    public Task<L.GeneratedCatalog> GenerateUnsignedCatalogAsync(
        Amd26111TwoPhasePreparation.PreparationReceipt prepared, CancellationToken token)
    {
        Calls.Add("GenerateCatalog");
        return Task.FromResult(new L.GeneratedCatalog(
            "FAKE_CATALOG", new string('C', 64), 194, false));
    }
    public Task<L.BoundDriver> SignOnceStageBindAndVerifyDualTrustAsync(
        L.Device device, Amd26111TwoPhasePreparation.PreparationReceipt prepared,
        L.GeneratedCatalog generated, L.Baseline baseline, CancellationToken token)
    {
        Calls.Add("Bind");
        return Task.FromResult(new L.BoundDriver(
            "oem49.inf", OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion,
            new string('D', 64), DualTrustedCount, true, true, true));
    }
    public Task<L.PrebootObservation> ObservePrebootAsync(
        L.Device device, L.BoundDriver bound, CancellationToken token)
    {
        Calls.Add("Preboot");
        return Task.FromResult(new L.PrebootObservation(
            device.InstanceId, bound.PublishedInf, bound.TargetDriverVersion,
            43, L.PrebootState.Code43AwaitingPostboot));
    }
    public Task<L.SoftwareResult> InstallVerifiedAmdSoftwareAsync(
        OfficialAmdInstallerVerifier.VerifiedSource source, L.BoundDriver bound,
        CancellationToken token)
    {
        Calls.Add("Software");
        return Task.FromResult(new L.SoftwareResult(!FailSoftware,
            FailSoftware ? "MSI rejected" : "MSI installed and verified", FailSoftware ? 1603 : 0));
    }
    public Task<L.RegisteredPostboot> RegisterPostbootAsync(
        L.PendingPostboot pending, L.AssignedDriver original, L.Baseline baseline,
        CancellationToken token)
    {
        Calls.Add("RegisterPostboot");
        if (pending.OriginalDriverVersion != original.Version ||
            pending.RegisteredBootIdentity != "boot-A" ||
            pending.OriginalPublishedInf != original.PublishedInf)
            throw new InvalidDataException("Fake pending ticket is incomplete");
        return Task.FromResult(new L.RegisteredPostboot(
            pending.TransactionId, true));
    }
    public Task<string> ReadPreRebootBootIdentityAsync(CancellationToken token)
    {
        Calls.Add("ReadBoot");
        return Task.FromResult("boot-A");
    }
    public Task RequestSingleRestartAsync(
        L.RegisteredPostboot registration, CancellationToken token)
    {
        Calls.Add("Reboot");
        return Task.CompletedTask;
    }
    public Task<L.RecoveryResult> RestoreBaselineAfterPrebootFailureAsync(
        L.Device device, L.AssignedDriver original, L.Baseline baseline,
        CancellationToken token)
    {
        Calls.Add("PrebootRollback");
        return Task.FromResult(new L.RecoveryResult(
            true, original.PublishedInf, 0));
    }
    public Task<L.PostbootObservation> CaptureFirstObservationAsync(
        L.PendingPostboot pending, CancellationToken token)
    {
        Calls.Add("ObserveFirstPostboot");
        return Task.FromResult(new L.PostbootObservation(
            pending.InstanceId, pending.ExpectedPublishedInf,
            pending.ExpectedDriverVersion, PostbootProblemCode, true, PostbootIdentity));
    }
    public Task<L.RecoveryResult> RecoverBaselineAsync(
        L.PendingPostboot pending, L.PostbootObservation first, CancellationToken token)
    {
        Calls.Add("RecoverPostboot");
        return Task.FromResult(new L.RecoveryResult(true, pending.OriginalPublishedInf, 0));
    }
}

sealed class FakeStep8Phases : M195Step8Sequence.IActions
{
    public List<string> Calls { get; } = [];
    public bool SignInvalid { get; init; }
    public bool StageInvalid { get; init; }
    public bool WrongBoundVersion { get; init; }
    public bool AuditIncomplete { get; init; }
    public bool AuditThrowsCritical { get; init; }

    public Task<M195Step8Sequence.SignedPackage> SignOnceAsync(
        CancellationToken token)
    {
        Calls.Add("Sign");
        return Task.FromResult(new M195Step8Sequence.SignedPackage(
            new string('A', 64), !SignInvalid, true));
    }

    public Task<M195Step8Sequence.StagedPackage> StageAsync(
        M195Step8Sequence.SignedPackage signed, CancellationToken token)
    {
        Calls.Add("Stage");
        return Task.FromResult(new M195Step8Sequence.StagedPackage(
            "oem91.inf", signed.CatalogSha256, !StageInvalid));
    }

    public Task<M195Step8Sequence.BoundAssignment> BindAsync(
        M195Step8Sequence.StagedPackage staged, CancellationToken token)
    {
        Calls.Add("Bind");
        return Task.FromResult(new M195Step8Sequence.BoundAssignment(
            staged.PublishedInf,
            WrongBoundVersion ? "0.0.0.0" :
                OfficialAmd26111SourceContract.VerifiedFrozenM194.DriverVersion,
            43, true, true));
    }

    public Task<M195Step8Sequence.TrustEvidence> AuditAndRegisterOriginalAsync(
        M195Step8Sequence.BoundAssignment bound, CancellationToken token)
    {
        Calls.Add("Audit");
        if (AuditThrowsCritical)
            throw new InvalidDataException("M195_AUDIT_GRAPHICS_HASH_MISMATCH");
        return Task.FromResult(new M195Step8Sequence.TrustEvidence(
            14, 14, AuditIncomplete ? 13 : 14, 14,
            !AuditIncomplete, true, "FAKE_STEP8_AUDIT",
            AuditIncomplete ? "M191 registration unverified" : null));
    }

    public Task RestoreAfterPostBindFailureAsync(
        M195Step8Sequence.BoundAssignment bound, Exception originalError)
    {
        Calls.Add("Recover");
        return Task.CompletedTask;
    }
}
