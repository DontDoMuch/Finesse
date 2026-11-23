using System.Buffers.Binary;
using Finesse.Compatibility;

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

Console.WriteLine($"OFFLINE_COMPATIBILITY_TESTS_PASS={passed}");
