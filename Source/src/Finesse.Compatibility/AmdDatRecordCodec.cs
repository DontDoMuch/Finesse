using System.Buffers.Binary;

namespace Finesse.Compatibility;

/// <summary>
/// Device/revision pair decoded from a position-dependent AMD amdgcf.dat record.
/// Does not contain or embed any AMD driver bytes.
/// </summary>
public readonly record struct DatDeviceRecord(ushort DeviceId, byte Revision);

public sealed record DatRecordAnalysis(
    int Count,
    ulong OriginalReleaseHeader,
    IReadOnlyList<DatDeviceRecord> Records);

/// <summary>
/// Read-only parser and record-payload encoder for the EXACT 12-byte header /
/// 3-byte positional-record layout observed in AMD 26.9.2 and 26.11.1.
/// This is not a universal DAT schema. Unknown formats must fail closed.
///
/// IMPORTANT: The 8-byte header depends on AMD's exact release marker and
/// MetroHash64. This class deliberately DOES NOT emit a full DAT file.
/// An independently validated release-specific header generator is required
/// before producing an installable candidate.
/// </summary>
public static class AmdDatRecordCodec
{
    public const int HeaderBytes = 12;
    public const int RecordBytes = 3;
    private const int MaxSupportedRecords = 4_000;

    public static DatRecordAnalysis Decode(byte[] dat)
    {
        ArgumentNullException.ThrowIfNull(dat);
        if (dat.Length < HeaderBytes)
            throw new InvalidDataException("AMD_DAT_SHORT_HEADER");

        uint countRaw = BinaryPrimitives.ReadUInt32LittleEndian(dat.AsSpan(0, 4));
        if (countRaw == 0 || countRaw > MaxSupportedRecords)
            throw new InvalidDataException("AMD_DAT_UNSUPPORTED_RECORD_COUNT");

        int count = checked((int)countRaw);
        if (dat.Length != checked(HeaderBytes + count * RecordBytes))
            throw new InvalidDataException("AMD_DAT_LENGTH_COUNT_MISMATCH");

        ulong releaseHeader = BinaryPrimitives.ReadUInt64BigEndian(dat.AsSpan(4, 8));
        var rows = new DatDeviceRecord[count];
        DatDeviceRecord? previous = null;
        for (int i = 0; i < count; i++)
        {
            int position = HeaderBytes + i * RecordBytes;
            uint encoded = BinaryPrimitives.ReadUInt16LittleEndian(dat.AsSpan(position, 2));
            if (encoded < position)
                throw new InvalidDataException("AMD_DAT_NEGATIVE_DECODED_DEVICE");

            uint device = encoded - (uint)position;
            if (device > ushort.MaxValue)
                throw new InvalidDataException("AMD_DAT_DEVICE_OUT_OF_RANGE");
            var row = new DatDeviceRecord((ushort)device, dat[position + 2]);
            if (previous is not null && Compare(previous.Value, row) >= 0)
                throw new InvalidDataException("AMD_DAT_UNSORTED_OR_DUPLICATE_ROW");
            rows[i] = row;
            previous = row;
        }

        return new DatRecordAnalysis(count, releaseHeader, Array.AsReadOnly(rows));
    }

    /// <summary>
    /// Deterministically merges target records into the original decoded rows.
    /// Does not change header bytes or claim a rebuilt DAT is ready to install.
    /// </summary>
    public static IReadOnlyList<DatDeviceRecord> Merge(
        DatRecordAnalysis original,
        IEnumerable<DatDeviceRecord> additions)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(additions);
        var sorted = new SortedSet<DatDeviceRecord>(
            Comparer<DatDeviceRecord>.Create(Compare));
        foreach (DatDeviceRecord row in original.Records)
            if (!sorted.Add(row))
                throw new InvalidDataException("AMD_DAT_ORIGINAL_DUPLICATE");
        foreach (DatDeviceRecord extra in additions)
            sorted.Add(extra);
        if (sorted.Count > MaxSupportedRecords)
            throw new InvalidDataException("AMD_DAT_TOO_MANY_RECORDS");
        return Array.AsReadOnly(sorted.ToArray());
    }

    /// <summary>
    /// Creates record payload ONLY (count/header intentionally excluded).
    /// A separate, verified release-specific checksum header is mandatory.
    /// </summary>
    public static byte[] EncodeRecordPayload(IReadOnlyList<DatDeviceRecord> sortedRows)
    {
        ArgumentNullException.ThrowIfNull(sortedRows);
        if (sortedRows.Count is < 1 or > MaxSupportedRecords)
            throw new InvalidDataException("AMD_DAT_UNSUPPORTED_RECORD_COUNT");

        byte[] payload = new byte[checked(sortedRows.Count * RecordBytes)];
        DatDeviceRecord? previous = null;
        for (int i = 0; i < sortedRows.Count; i++)
        {
            DatDeviceRecord row = sortedRows[i];
            if (previous is not null && Compare(previous.Value, row) >= 0)
                throw new InvalidDataException("AMD_DAT_UNSORTED_OR_DUPLICATE_ROW");

            int absolutePosition = HeaderBytes + i * RecordBytes;
            int encoded = checked(absolutePosition + row.DeviceId);
            if (encoded > ushort.MaxValue)
                throw new InvalidDataException("AMD_DAT_ENCODED_DEVICE_OVERFLOW");

            BinaryPrimitives.WriteUInt16LittleEndian(
                payload.AsSpan(i * RecordBytes, 2), (ushort)encoded);
            payload[i * RecordBytes + 2] = row.Revision;
            previous = row;
        }
        return payload;
    }

    private static int Compare(DatDeviceRecord left, DatDeviceRecord right)
    {
        int deviceComparison = left.DeviceId.CompareTo(right.DeviceId);
        return deviceComparison != 0
            ? deviceComparison
            : left.Revision.CompareTo(right.Revision);
    }
}
