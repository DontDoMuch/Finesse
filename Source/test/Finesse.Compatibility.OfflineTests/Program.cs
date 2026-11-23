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

Console.WriteLine($"OFFLINE_COMPATIBILITY_TESTS_PASS={passed}");
