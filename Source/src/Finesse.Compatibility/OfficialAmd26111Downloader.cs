using System.Net;
using System.Security.Cryptography;

namespace Finesse.Compatibility;

/// <summary>
/// Official AMD 26.11.1 acquisition only. Uses the exact M194 installer pin,
/// HTTPS host/path allowlist, explicit redirects, source length, SHA-256 and
/// WinVerifyTrust. Does not run the installer or modify GPU/Windows trust.
/// </summary>
public static class OfficialAmd26111Downloader
{
    private static readonly HashSet<string> AllowedHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "drivers.amd.com",
            "download.amd.com"
        };

    private static readonly Uri[] ApprovedReferers =
    [
        new("https://www.amd.com/en/resources/support-articles/release-notes/RN-RAD-WIN-26-11-1.html"),
        new("https://www.amd.com/en/support/download/drivers.html")
    ];

    public static bool IsApprovedDownloadUri(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
            return false;
        string name = OfficialAmd26111SourceContract.VerifiedFrozenM194.InstallerFileName;
        return uri.Scheme == Uri.UriSchemeHttps &&
            uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
            AllowedHosts.Contains(uri.DnsSafeHost) &&
            uri.AbsolutePath.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAmdIncompleteRedirect(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.DnsSafeHost.Equals("www.amd.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.EndsWith("/Download-Incomplete.html",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Downloads into a NEW isolated directory, atomically publishing it only
    /// after every integrity/Windows trust check. May throw if AMD changes the
    /// approved URL or denies referer access. No fallback to mirror hosts.
    /// </summary>
    public static async Task<OfficialAmdInstallerVerifier.VerifiedSource>
        DownloadFreshAsync(string freshOutputDirectory,
            IProgress<(long Copied, long Total)>? progress = null,
            CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("AMD source trust checks require Windows.");
        ArgumentException.ThrowIfNullOrWhiteSpace(freshOutputDirectory);
        string destination = Path.GetFullPath(freshOutputDirectory);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("M195_AMD_DOWNLOAD_OUTPUT_EXISTS");
        string parent = Path.GetDirectoryName(destination) ??
            throw new InvalidDataException("M195_AMD_DOWNLOAD_NO_PARENT");
        RejectReparseAncestors(parent);

        var contract = OfficialAmd26111SourceContract.VerifiedFrozenM194;
        string temp = Path.Combine(parent,
            ".finesse-m195-amd-download-" + Guid.NewGuid().ToString("N"));
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            CheckCertificateRevocationList = true
        };
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Finesse-M195/1.0 (Windows 11; verified official AMD installer)");
        var initial = new Uri("https://drivers.amd.com/drivers/" + contract.InstallerFileName);
        try
        {
            Directory.CreateDirectory(temp);
            string localFile = Path.Combine(temp, contract.InstallerFileName);
            for (int attempt = 0; attempt < ApprovedReferers.Length; attempt++)
            {
                Uri current = initial;
                bool retryOtherReferer = false;
                var visited = new HashSet<string>(StringComparer.Ordinal);
                for (int redirect = 0; redirect < 6; redirect++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsApprovedDownloadUri(current))
                        throw new InvalidDataException("M195_AMD_DOWNLOAD_URL_NOT_APPROVED");
                    if (!visited.Add(current.AbsoluteUri))
                        throw new InvalidDataException("M195_AMD_DOWNLOAD_REDIRECT_LOOP");

                    using var request = new HttpRequestMessage(HttpMethod.Get, current);
                    request.Headers.Referrer = ApprovedReferers[attempt];
                    request.Headers.Accept.ParseAdd("*/*");
                    using var response = await client.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    if (response.StatusCode is
                        HttpStatusCode.Moved or HttpStatusCode.Redirect or
                        HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or
                        HttpStatusCode.PermanentRedirect)
                    {
                        Uri next = new(current, response.Headers.Location ??
                            throw new InvalidDataException("M195_AMD_REDIRECT_WITHOUT_LOCATION"));
                        if (!IsApprovedDownloadUri(next))
                        {
                            if (IsAmdIncompleteRedirect(next) &&
                                attempt + 1 < ApprovedReferers.Length)
                            {
                                retryOtherReferer = true;
                                break;
                            }
                            throw new InvalidDataException(
                                "M195_AMD_REDIRECT_LEFT_OFFICIAL_INSTALLER_ALLOWLIST");
                        }
                        current = next;
                        continue;
                    }
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentLength is long length &&
                        length != contract.ExactInstallerBytes)
                        throw new InvalidDataException("M195_AMD_DOWNLOAD_CONTENT_LENGTH_MISMATCH");

                    using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    long copied = 0;
                    await using (var incoming = await response.Content.ReadAsStreamAsync(
                        cancellationToken))
                    await using (var output = new FileStream(localFile,
                        FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        byte[] buffer = new byte[1024 * 1024];
                        int read;
                        while ((read = await incoming.ReadAsync(buffer.AsMemory(),
                            cancellationToken)) != 0)
                        {
                            copied = checked(copied + read);
                            if (copied > contract.ExactInstallerBytes)
                                throw new InvalidDataException("M195_AMD_DOWNLOAD_EXCEEDED_PINNED_BYTES");
                            hasher.AppendData(buffer, 0, read);
                            await output.WriteAsync(buffer.AsMemory(0, read),
                                cancellationToken);
                            progress?.Report((copied, contract.ExactInstallerBytes));
                        }
                        await output.FlushAsync(cancellationToken);
                    }
                    if (copied != contract.ExactInstallerBytes ||
                        Convert.ToHexString(hasher.GetHashAndReset()) !=
                            contract.InstallerSha256)
                        throw new InvalidDataException("M195_AMD_DOWNLOADED_BYTES_FAILED_PIN");

                    // The validated source is the *same basename* expected by
                    // Verify26111Async, even before atomic directory publish.
                    _ = await OfficialAmdInstallerVerifier.Verify26111Async(
                        localFile, cancellationToken);
                    Directory.Move(temp, destination);
                    try
                    {
                        return await OfficialAmdInstallerVerifier.Verify26111Async(
                            Path.Combine(destination, contract.InstallerFileName),
                            cancellationToken);
                    }
                    catch
                    {
                        // Destination was created entirely by this attempt.
                        // Never leave an unverified 'completed' download folder.
                        if (Directory.Exists(destination))
                            Directory.Delete(destination, recursive: true);
                        throw;
                    }
                }
                if (retryOtherReferer)
                    continue;
                throw new InvalidDataException("M195_AMD_DOWNLOAD_REDIRECT_LIMIT");
            }
            throw new InvalidDataException("M195_AMD_OFFICIAL_REFERER_REJECTED");
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, recursive: true);
        }
    }

    private static void RejectReparseAncestors(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((Directory.Exists(cursor) || File.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_AMD_DOWNLOAD_REPARSE_PARENT");
            string? parent = Path.GetDirectoryName(cursor);
            if (string.Equals(cursor, parent, StringComparison.OrdinalIgnoreCase))
                break;
            cursor = parent;
        }
    }
}
