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

Console.WriteLine($"OFFLINE_COMPATIBILITY_TESTS_PASS={passed}");
