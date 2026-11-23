using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Finesse.Compatibility;

/// <summary>
/// Read-only diagnostic collection of M195 postboot JSON/log receipts.
/// Creates a new SHA-256-sidecar ZIP under the existing user's
/// Downloads/Finesse/Reports without altering any evidence source, GPU,
/// Windows trust, MSI installation, scheduled task, or reboot decision.
/// </summary>
internal static class M195PostbootEvidenceExporter
{
    internal sealed record ExportResult(
        string ZipFile, string ZipSha256, string Sha256Sidecar,
        int FilesIncluded, string TransactionId);

    internal static async Task<ExportResult> ExportAsync(
        string sourceReportsDirectory, string transactionId,
        string? destinationRoot = null, CancellationToken token = default)
    {
        if (!Guid.TryParseExact(transactionId, "N", out Guid guid) ||
            !string.Equals(guid.ToString("N"), transactionId,
                StringComparison.Ordinal))
            throw new InvalidDataException("M195_EXPORT_INVALID_TRANSACTION_ID");
        string source = Path.GetFullPath(sourceReportsDirectory);
        RejectReparseAncestors(source);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(source);
        string[] all = Directory.GetFiles(source, "*",
            SearchOption.TopDirectoryOnly);
        string[] included = all.Where(p =>
            Path.GetFileName(p).StartsWith("M195-", StringComparison.Ordinal) &&
            (p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
             p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
             p.EndsWith(".log", StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        if (included.Length < 1 || included.Length > 200 ||
            !included.Any(p => Path.GetFileName(p).StartsWith(
                "M195-FirstPostboot-", StringComparison.Ordinal) &&
                p.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("M195_EXPORT_NO_IMMUTABLE_FIRST_SNAPSHOT");
        long totalBytes = 0;
        foreach (string file in included)
        {
            token.ThrowIfCancellationRequested();
            RejectReparseAncestors(file);
            long bytes = new FileInfo(file).Length;
            if (bytes is < 1 or > 30_000_000)
                throw new InvalidDataException("M195_EXPORT_EVIDENCE_SIZE_INVALID");
            totalBytes = checked(totalBytes + bytes);
            if (totalBytes > 120_000_000)
                throw new InvalidDataException("M195_EXPORT_TOTAL_SIZE_TOO_LARGE");
        }
        string reports = Path.GetFullPath(destinationRoot ??
            Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
                "Downloads", "Finesse", "Reports"));
        RejectReparseAncestors(reports);
        Directory.CreateDirectory(reports);
        string name = "Finesse-M195-Postboot-" + transactionId;
        string finalZip = Path.Combine(reports, name + ".zip");
        string shaFile = Path.Combine(reports, name + ".zip.sha256");
        string temporary = Path.Combine(reports, "." + name +
            "-" + Guid.NewGuid().ToString("N") + ".tmp");
        if (File.Exists(finalZip) || File.Exists(shaFile))
            throw new IOException("M195_EXPORT_TRANSACTION_ALREADY_PUBLISHED");
        try
        {
            var inventory = new List<object>();
            using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create,
                leaveOpen: false))
            {
                foreach (string file in included)
                {
                    token.ThrowIfCancellationRequested();
                    string entryName = Path.GetFileName(file);
                    ZipArchiveEntry entry = archive.CreateEntry(entryName,
                        CompressionLevel.Optimal);
                    await using (Stream output = entry.Open())
                    await using (FileStream input = new FileStream(file,
                        FileMode.Open, FileAccess.Read, FileShare.Read))
                        await input.CopyToAsync(output, token);
                    using FileStream hasher = File.OpenRead(file);
                    string hash = Convert.ToHexString(SHA256.HashData(hasher));
                    inventory.Add(new {
                        File = entryName, Bytes = new FileInfo(file).Length,
                        Sha256 = hash
                    });
                }
                ZipArchiveEntry manifest = archive.CreateEntry(
                    "M195-Export-Manifest.json", CompressionLevel.Optimal);
                await using Stream outStream = manifest.Open();
                byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                    Schema = "Finesse.M195.PostbootEvidenceZip.v1",
                    TransactionId = transactionId,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    SourceEvidencePreserved = true,
                    NoDriverOrSoftwareMutation = true,
                    Files = inventory
                }, new JsonSerializerOptions { WriteIndented = true }));
                await outStream.WriteAsync(json, token);
            }
            using FileStream zipBytes = File.OpenRead(temporary);
            string zipSha = Convert.ToHexString(SHA256.HashData(zipBytes));
            File.Move(temporary, finalZip, overwrite: false);
            await File.WriteAllTextAsync(shaFile,
                zipSha + "  " + Path.GetFileName(finalZip) + "\n",
                new UTF8Encoding(false), token);
            return new ExportResult(finalZip, zipSha,
                shaFile, included.Length, transactionId);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static void RejectReparseAncestors(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((Directory.Exists(cursor) || File.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_EXPORT_REPARSE_FILE_OR_DIRECTORY");
            string? parent = Path.GetDirectoryName(cursor);
            if (string.Equals(cursor, parent, StringComparison.OrdinalIgnoreCase))
                break;
            cursor = parent;
        }
    }
}
