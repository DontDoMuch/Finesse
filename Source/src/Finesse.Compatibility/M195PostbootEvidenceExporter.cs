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
        int FilesIncluded, string TransactionId,
        int PrebootFilesIncluded = 0);

    internal static async Task<ExportResult> ExportAsync(
        string sourceReportsDirectory, string transactionId,
        string? destinationRoot = null, CancellationToken token = default,
        string? prebootSessionDirectory = null)
    {
        if (!Guid.TryParseExact(transactionId, "N", out Guid guid) ||
            !string.Equals(guid.ToString("N"), transactionId,
                StringComparison.Ordinal))
            throw new InvalidDataException("M195_EXPORT_INVALID_TRANSACTION_ID");
        string source = Path.GetFullPath(sourceReportsDirectory);
        RejectReparseAncestors(source);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException(source);
        // Preserve the original root filenames for existing postboot reports.
        // Optional preboot receipts live in an admin-owned session workspace;
        // only explicitly named diagnostics are copied. NO .INF/.DAT/.CAT,
        // .DLL/.SYS/.EXE, private keys, driver-store exports or vendor files.
        var included = new List<(string Absolute, string Entry)>();
        bool firstFound = false;
        foreach (string path in Directory.GetFiles(source, "*",
            SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            if (!IsDiagnosticFile(name) ||
                !name.StartsWith("M195-", StringComparison.Ordinal))
                continue;
            included.Add((path, name));
            firstFound |= name.StartsWith("M195-FirstPostboot-",
                StringComparison.Ordinal) &&
                name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }
        if (!firstFound)
            throw new InvalidDataException("M195_EXPORT_NO_IMMUTABLE_FIRST_SNAPSHOT");

        int prebootIncluded = 0;
        bool prebootAvailable = false;
        if (!string.IsNullOrWhiteSpace(prebootSessionDirectory))
        {
            string preboot = Path.GetFullPath(prebootSessionDirectory);
            RejectReparseAncestors(preboot);
            if (Directory.Exists(preboot))
            {
                prebootAvailable = true;
                var pending = new Stack<string>();
                pending.Push(preboot);
                int scannedDirectories = 0;
                while (pending.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    string current = pending.Pop();
                    if (++scannedDirectories > 2000)
                        throw new InvalidDataException("M195_EXPORT_TOO_MANY_SESSION_DIRECTORIES");
                    foreach (string directory in Directory.EnumerateDirectories(current))
                    {
                        RejectReparseAncestors(directory);
                        pending.Push(directory);
                    }
                    foreach (string file in Directory.EnumerateFiles(current, "*",
                        SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(file);
                        if (!IsDiagnosticFile(name) || !IsPrebootReceiptName(name))
                            continue;
                        string relative = Path.GetRelativePath(preboot, file).Replace('\\', '/');
                        if (relative.StartsWith("../", StringComparison.Ordinal) ||
                            relative.StartsWith("/", StringComparison.Ordinal) ||
                            relative.Split('/').Any(segment =>
                                segment is "." or ".." or ""))
                            throw new InvalidDataException("M195_EXPORT_PREBOOT_PATH_ESCAPE");
                        included.Add((file, "Preboot/" + relative));
                        prebootIncluded++;
                    }
                }
            }
        }
        if (included.Count < 1 || included.Count > 500 ||
            included.Select(x => x.Entry).Distinct(
                StringComparer.OrdinalIgnoreCase).Count() != included.Count)
            throw new InvalidDataException("M195_EXPORT_EVIDENCE_COUNT_OR_DUPLICATE");
        included.Sort((x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Entry, y.Entry));
        long totalBytes = 0;
        foreach (var item in included)
        {
            token.ThrowIfCancellationRequested();
            RejectReparseAncestors(item.Absolute);
            long bytes = new FileInfo(item.Absolute).Length;
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
        string archiveBaseName = "Finesse-M195-Postboot-" + transactionId;
        string finalZip = Path.Combine(reports, archiveBaseName + ".zip");
        string shaFile = Path.Combine(reports, archiveBaseName + ".zip.sha256");
        string temporary = Path.Combine(reports, "." + archiveBaseName +
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
                foreach (var item in included)
                {
                    token.ThrowIfCancellationRequested();
                    ZipArchiveEntry entry = archive.CreateEntry(item.Entry,
                        CompressionLevel.Optimal);
                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long count = 0;
                    await using (Stream output = entry.Open())
                    await using (FileStream input = new FileStream(item.Absolute,
                        FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        byte[] buffer = new byte[64 * 1024];
                        int read;
                        while ((read = await input.ReadAsync(buffer, token)) > 0)
                        {
                            count = checked(count + read);
                            if (count > 30_000_000)
                                throw new InvalidDataException("M195_EXPORT_FILE_CHANGED_OR_TOO_LARGE");
                            hasher.AppendData(buffer, 0, read);
                            await output.WriteAsync(buffer.AsMemory(0, read), token);
                        }
                    }
                    string hash = Convert.ToHexString(hasher.GetHashAndReset());
                    inventory.Add(new { File = item.Entry, Bytes = count, Sha256 = hash });
                }
                ZipArchiveEntry manifest = archive.CreateEntry(
                    "M195-Export-Manifest.json", CompressionLevel.Optimal);
                await using Stream outStream = manifest.Open();
                byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
                    Schema = "Finesse.M195.PostbootEvidenceZip.v1",
                    TransactionId = transactionId,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    SourceEvidencePreserved = true,
                    PrebootSourceAvailable = prebootAvailable,
                    PrebootFilesIncluded = prebootIncluded,
                    PostbootFilesIncluded = included.Count - prebootIncluded,
                    NoDriverOrSoftwareMutation = true,
                    VendorDriverBinariesIncluded = false,
                    Files = inventory
                }, new JsonSerializerOptions { WriteIndented = true }));
                await outStream.WriteAsync(json, token);
            }
            string zipSha;
            using (FileStream zipBytes = File.OpenRead(temporary))
                zipSha = Convert.ToHexString(SHA256.HashData(zipBytes));
            // The SHA reader MUST be closed before rename on Windows, which
            // denies a File.Move while our own FileShare.Read handle is open.
            File.Move(temporary, finalZip, overwrite: false);
            await File.WriteAllTextAsync(shaFile,
                zipSha + "  " + Path.GetFileName(finalZip) + "\n",
                new UTF8Encoding(false), token);
            return new ExportResult(finalZip, zipSha,
                shaFile, included.Count, transactionId, prebootIncluded);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static bool IsDiagnosticFile(string name) =>
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrebootReceiptName(string name) =>
        name.StartsWith("M195-", StringComparison.Ordinal) ||
        name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("SelectedOriginalAMD.txt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("7zr-stdout.txt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("7zr-stderr.txt", StringComparison.OrdinalIgnoreCase);

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
