using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Finesse.Compatibility;

/// <summary>
/// Observed AMD 26.9.2 / 26.11.1 amdgcf.dat checksum recipe. This is an
/// implementation of the MetroHash64 variant preserved in the working 26.9.2
/// source, not a claim that all future AMD DAT schemas use the same hash.
/// No AMD binaries are embedded.
/// </summary>
public static class AmdDatReleaseRebuilder
{
    private const ulong K0 = 0xD6D018F5ul;
    private const ulong K1 = 0xA2AA033Bul;
    private const ulong K2 = 0x62992FC1ul;
    private const ulong K3 = 0x30BC5B29ul;

    /// <summary>MetroHash64 variant used by the frozen 26.9.2 builder.</summary>
    public static ulong MetroHash64(ReadOnlySpan<byte> data)
    {
        unchecked
        {
            int offset = 0;
            int length = data.Length;
            ulong hash = K2 * K0;

            if (length >= 32)
            {
                ulong v0 = hash, v1 = hash, v2 = hash, v3 = hash;
                int end = length - 32;
                while (offset <= end)
                {
                    v0 += BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)) * K0;
                    offset += 8;
                    v0 = BitOperations.RotateRight(v0, 29) + v2;

                    v1 += BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)) * K1;
                    offset += 8;
                    v1 = BitOperations.RotateRight(v1, 29) + v3;

                    v2 += BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)) * K2;
                    offset += 8;
                    v2 = BitOperations.RotateRight(v2, 29) + v0;

                    v3 += BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)) * K3;
                    offset += 8;
                    v3 = BitOperations.RotateRight(v3, 29) + v1;
                }
                v2 ^= BitOperations.RotateRight(((v0 + v3) * K0) + v1, 37) * K1;
                v3 ^= BitOperations.RotateRight(((v1 + v2) * K1) + v0, 37) * K0;
                v0 ^= BitOperations.RotateRight(((v0 + v2) * K0) + v3, 37) * K1;
                v1 ^= BitOperations.RotateRight(((v1 + v3) * K1) + v2, 37) * K0;
                hash += v0 ^ v1;
            }

            int remaining = length - offset;
            if (remaining >= 16)
            {
                ulong v0 = hash + BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)) * K2;
                offset += 8;
                v0 = BitOperations.RotateRight(v0, 29) * K3;

                ulong v1 = hash + BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)) * K2;
                offset += 8;
                v1 = BitOperations.RotateRight(v1, 29) * K3;

                v0 ^= BitOperations.RotateRight(v0 * K0, 21) + v1;
                v1 ^= BitOperations.RotateRight(v1 * K3, 21) + v0;
                hash += v1;
                remaining = length - offset;
            }
            if (remaining >= 8)
            {
                hash += BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)) * K3;
                offset += 8;
                hash ^= BitOperations.RotateRight(hash, 55) * K1;
                remaining = length - offset;
            }
            if (remaining >= 4)
            {
                hash += BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)) * K3;
                offset += 4;
                hash ^= BitOperations.RotateRight(hash, 26) * K1;
                remaining = length - offset;
            }
            if (remaining >= 2)
            {
                hash += BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2)) * K3;
                offset += 2;
                hash ^= BitOperations.RotateRight(hash, 48) * K1;
                remaining = length - offset;
            }
            if (remaining >= 1)
            {
                hash += (ulong)data[offset] * K3;
                hash ^= BitOperations.RotateRight(hash, 37) * K1;
            }
            hash ^= BitOperations.RotateRight(hash, 28);
            hash *= K0;
            hash ^= BitOperations.RotateRight(hash, 29);
            return hash;
        }
    }

    /// <summary>
    /// Hash input = positional record payload followed by the exact AMD release
    /// marker, encoded as ASCII. The stored header is BIG endian.
    /// </summary>
    public static ulong ComputeReleaseHeader(
        ReadOnlySpan<byte> payload, string exactAmdReleaseMarker)
    {
        if (string.IsNullOrWhiteSpace(exactAmdReleaseMarker) ||
            exactAmdReleaseMarker.Any(ch => ch > 0x7f))
            throw new InvalidDataException("AMD_DAT_INVALID_RELEASE_MARKER");

        byte[] versionBytes = Encoding.ASCII.GetBytes(exactAmdReleaseMarker);
        byte[] input = new byte[checked(payload.Length + versionBytes.Length)];
        payload.CopyTo(input);
        versionBytes.CopyTo(input, payload.Length);
        return MetroHash64(input);
    }

    /// <summary>
    /// Verify original AMD checksum before doing any modification. Incorrect
    /// version markers, layout and unverified original files are rejected.
    /// </summary>
    public static DatRecordAnalysis VerifyOriginal(
        byte[] originalDat, string exactAmdReleaseMarker)
    {
        DatRecordAnalysis decoded = AmdDatRecordCodec.Decode(originalDat);
        ulong expected = ComputeReleaseHeader(
            originalDat.AsSpan(AmdDatRecordCodec.HeaderBytes), exactAmdReleaseMarker);
        if (expected != decoded.OriginalReleaseHeader)
            throw new InvalidDataException("AMD_DAT_ORIGINAL_METROHASH_RELEASE_MISMATCH");
        return decoded;
    }

    /// <summary>
    /// Build only for an exact verified physical hardware profile. The source
    /// must be an authentic AMD DAT supplied by an external verified acquisition
    /// layer, not a bundled or untrusted DAT. Do not skip catalog regeneration.
    /// </summary>
    public static byte[] BuildForVerifiedProfile(
        byte[] originalDat,
        string exactAmdReleaseMarker,
        HandheldProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (HandheldProfiles.Resolve(profile.HardwareId) != profile)
            throw new InvalidDataException("AMD_DAT_UNSUPPORTED_OR_MODIFIED_PROFILE");

        DatRecordAnalysis original = VerifyOriginal(originalDat, exactAmdReleaseMarker);
        IReadOnlyList<DatDeviceRecord> rows = AmdDatRecordCodec.Merge(original,
        [
            new DatDeviceRecord(profile.DeviceId, profile.Revision),
            new DatDeviceRecord(0x15C8, 0xC9)
        ]);
        byte[] payload = AmdDatRecordCodec.EncodeRecordPayload(rows);
        byte[] updated = new byte[checked(AmdDatRecordCodec.HeaderBytes + payload.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(updated.AsSpan(0, 4), (uint)rows.Count);
        BinaryPrimitives.WriteUInt64BigEndian(updated.AsSpan(4, 8),
            ComputeReleaseHeader(payload, exactAmdReleaseMarker));
        payload.CopyTo(updated, AmdDatRecordCodec.HeaderBytes);

        DatRecordAnalysis final = VerifyOriginal(updated, exactAmdReleaseMarker);
        if (!final.Records.SequenceEqual(rows) ||
            !original.Records.All(final.Records.Contains) ||
            !final.Records.Contains(new DatDeviceRecord(profile.DeviceId, profile.Revision)) ||
            !final.Records.Contains(new DatDeviceRecord(0x15C8, 0xC9)) ||
            final.Count < original.Count || final.Count > original.Count + 2)
            throw new InvalidDataException("AMD_DAT_FINAL_ROUNDTRIP_FAILED");

        return updated;
    }
}
