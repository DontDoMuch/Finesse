using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;

namespace Finesse.Compatibility;

/// <summary>
/// Frozen M138 + M140 pinned Microsoft/7-Zip prerequisite acquisition.
/// No SDK, WDK or 7-Zip installer is executed or system-installed.
/// Cache contents are read only after SHA-256 and exact length validation.
/// </summary>
internal static class M195PinnedToolBootstrap
{
    internal sealed record Tools(
        string SevenZrExe, string Inf2CatExe, string SignToolExe,
        string Portable7ZipReleaseExe, string Root);

    internal sealed record Pin(
        string Name, string Url, long? Bytes, string Sha256, string RelativePath);

    private static readonly HashSet<string> Hosts =
        new(StringComparer.OrdinalIgnoreCase) {
            "www.nuget.org", "api.nuget.org", "globalcdn.nuget.org",
            "github.com", "release-assets.githubusercontent.com",
            "objects.githubusercontent.com"
        };
    private static readonly Pin[] Downloads = [
        new("7zr.exe",
            "https://github.com/ip7z/7zip/releases/download/26.04/7zr.exe",
            602_624,
            "256FECA8E274E5DA655E2A284FABAFD9F554365EB164862089DACD4E8276D282",
            "tools/7zr/26.04/7zr.exe"),
        new("Microsoft.Windows.SDK.BuildTools.10.0.28000.2526.nupkg",
            "https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools/10.0.28000.2526",
            22_238_762,
            "A09A4C9D68160CED4765137A9A7444EA560EA86C45D6A77093DEA58C2F7563A0",
            "m8-toolchain/packages/Microsoft.Windows.SDK.BuildTools.10.0.28000.2526.nupkg"),
        new("Microsoft.Windows.WDK.x64.10.0.28000.2526.nupkg",
            "https://www.nuget.org/api/v2/package/Microsoft.Windows.WDK.x64/10.0.28000.2526",
            113_683_738,
            "63C939FB5A79295BF40E941DB592681272219B04EDFF095FE2F3D123E5579A90",
            "m8-toolchain/packages/Microsoft.Windows.WDK.x64.10.0.28000.2526.nupkg"),
        new("7z2606-x64.exe",
            "https://github.com/ip7z/7zip/releases/download/26.06/7z2606-x64.exe",
            null,
            "9B697F65ECC2D9B0152F5EE96D72DDE522A7827690349D9307935DACF40F42EC",
            "tools/7zip/26.06/7z2606-x64.exe")
    ];

    private sealed record Member(
        string DownloadRelative, string MemberName, string Destination,
        long ExactBytes, string ExactSha256);

    private static readonly Member[] Members = [
        new("m8-toolchain/packages/Microsoft.Windows.SDK.BuildTools.10.0.28000.2526.nupkg",
            "bin/10.0.28000.0/x64/signtool.exe",
            "m8-toolchain/extracted-tools/SDK-28000/x64/signtool.exe",
            551_408, "80972965E7FC311D293222B1A0E2C1BFB60F363239173964DBE2A71638314B9F"),
        new("m8-toolchain/packages/Microsoft.Windows.WDK.x64.10.0.28000.2526.nupkg",
            "c/bin/10.0.28000.0/x86/Inf2Cat.exe",
            "m8-toolchain/extracted-tools/WDK-28000/x86/Inf2Cat.exe",
            34_880, "82C302FC9069783674B51665CE80E769F8500DB7FC737A4B8A3773C521950B86")
    ];

    internal static IReadOnlyList<Pin> FrozenDownloadPins => Downloads.AsReadOnly();

    internal static async Task<Tools> EnsureAsync(
        string? cacheRoot, IProgress<string>? progress,
        CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Windows pinned tool bootstrap requires Windows.");
        string root = Path.GetFullPath(cacheRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Finesse", "portable-cache"));
        Guard(root);
        Directory.CreateDirectory(root);
        using var handler = new HttpClientHandler {
            AllowAutoRedirect = false, UseCookies = false,
            CheckCertificateRevocationList = true,
            AutomaticDecompression = DecompressionMethods.None
        };
        using var client = new HttpClient(handler) {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Finesse-M195/1.0");
        foreach (Pin item in Downloads)
        {
            token.ThrowIfCancellationRequested();
            string file = Path.Combine(root, item.RelativePath.Replace('/',
                Path.DirectorySeparatorChar));
            Guard(file);
            if (await ExactAsync(file, item.Bytes, item.Sha256, token))
                continue;
            if (File.Exists(file))
                throw new InvalidDataException("M195_PINNED_TOOL_CACHE_CHANGED_" + item.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            string temp = file + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                await DownloadOneAsync(client, item, temp, progress, token);
                if (!await ExactAsync(temp, item.Bytes, item.Sha256, token))
                    throw new InvalidDataException("M195_PINNED_TOOL_HASH_FAILED_" + item.Name);
                File.Move(temp, file, overwrite: false);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        foreach (Member member in Members)
        {
            token.ThrowIfCancellationRequested();
            string dest = Path.Combine(root, member.Destination.Replace('/',
                Path.DirectorySeparatorChar));
            Guard(dest);
            if (await ExactAsync(dest, member.ExactBytes,
                    member.ExactSha256, token))
                continue;
            if (File.Exists(dest))
                throw new InvalidDataException("M195_PINNED_EXTRACTED_TOOL_CHANGED_" + dest);
            string package = Path.Combine(root, member.DownloadRelative.Replace('/',
                Path.DirectorySeparatorChar));
            Pin contract = Downloads.Single(p =>
                p.RelativePath.Equals(member.DownloadRelative, StringComparison.Ordinal));
            if (!await ExactAsync(package, contract.Bytes,
                    contract.Sha256, token))
                throw new InvalidDataException("M195_PINNED_NUGET_ARCHIVE_INVALID");
            Guard(package);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var zip = ZipFile.OpenRead(package);
            var matches = zip.Entries.Where(e =>
                string.Equals(e.FullName.Replace('\\', '/'), member.MemberName,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || matches[0].Length != member.ExactBytes)
                throw new InvalidDataException("M195_PINNED_TOOL_MEMBER_MISSING_OR_AMBIGUOUS");
            string part = dest + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var output = new FileStream(part, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                await using (var input = matches[0].Open())
                    await input.CopyToAsync(output, token);
                if (!await ExactAsync(part, member.ExactBytes,
                        member.ExactSha256, token))
                    throw new InvalidDataException("M195_PINNED_TOOL_MEMBER_SHA_FAILED");
                File.Move(part, dest, overwrite: false);
            }
            finally { if (File.Exists(part)) File.Delete(part); }
        }

        string SevenZr() => Path.Combine(root, Downloads[0].RelativePath.Replace('/',
            Path.DirectorySeparatorChar));
        string Zip26() => Path.Combine(root, Downloads[3].RelativePath.Replace('/',
            Path.DirectorySeparatorChar));
        string Sign() => Path.Combine(root, Members[0].Destination.Replace('/',
            Path.DirectorySeparatorChar));
        string Inf() => Path.Combine(root, Members[1].Destination.Replace('/',
            Path.DirectorySeparatorChar));
        if (!await ExactAsync(SevenZr(), Downloads[0].Bytes, Downloads[0].Sha256, token) ||
            !await ExactAsync(Zip26(), Downloads[3].Bytes, Downloads[3].Sha256, token) ||
            !await ExactAsync(Sign(), Members[0].ExactBytes, Members[0].ExactSha256, token) ||
            !await ExactAsync(Inf(), Members[1].ExactBytes, Members[1].ExactSha256, token))
            throw new InvalidDataException("M195_TOOLCHAIN_FINAL_VERIFICATION_FAILED");
        progress?.Report("Verified Windows driver tools");
        return new Tools(SevenZr(), Inf(), Sign(), Zip26(), root);
    }

    private static async Task DownloadOneAsync(
        HttpClient http, Pin pin, string temp, IProgress<string>? progress,
        CancellationToken token)
    {
        Uri current = new(pin.Url);
        for (int hop = 0; hop < 8; hop++)
        {
            if (current.Scheme != Uri.UriSchemeHttps || current.Port != 443 ||
                !Hosts.Contains(current.DnsSafeHost) ||
                !string.IsNullOrEmpty(current.UserInfo))
                throw new InvalidDataException("M195_TOOL_DOWNLOAD_LEFT_PINNED_HTTPS_HOSTS");
            using var response = await http.GetAsync(
                current, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or
                HttpStatusCode.Redirect or HttpStatusCode.SeeOther or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                current = new Uri(current, response.Headers.Location ??
                    throw new InvalidDataException("M195_TOOL_REDIRECT_WITHOUT_LOCATION"));
                continue;
            }
            if (response.StatusCode != HttpStatusCode.OK)
                throw new HttpRequestException(
                    "M195 pinned tool request failed: " + (int)response.StatusCode);
            if (pin.Bytes is long exact &&
                response.Content.Headers.ContentLength is long advertised &&
                advertised != exact)
                throw new InvalidDataException("M195_TOOL_CONTENT_LENGTH_CHANGED");
            progress?.Report("Verifying prerequisite: " + pin.Name);
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = new FileStream(temp, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 1024 * 1024,
                FileOptions.SequentialScan | FileOptions.Asynchronous);
            byte[] buf = new byte[1024 * 1024];
            long count = 0;
            int read;
            // The 7-Zip 26.06 file is SHA pinned but the frozen M140 source
            // did not specify its size. Limit it to 100MB to bound storage.
            long limit = pin.Bytes ?? 100_000_000L;
            while ((read = await input.ReadAsync(buf, token)) > 0)
            {
                count = checked(count + read);
                if (count > limit)
                    throw new InvalidDataException("M195_TOOL_OVERSIZE");
                await output.WriteAsync(buf.AsMemory(0, read), token);
            }
            if (pin.Bytes is long expected && count != expected)
                throw new InvalidDataException("M195_TOOL_INCOMPLETE_DOWNLOAD");
            return;
        }
        throw new InvalidDataException("M195_TOOL_TOO_MANY_REDIRECTS");
    }

    private static async Task<bool> ExactAsync(
        string path, long? exactBytes, string exactSha,
        CancellationToken token)
    {
        Guard(path);
        if (!File.Exists(path) ||
            (exactBytes is long expected && new FileInfo(path).Length != expected))
            return false;
        await using var read = new FileStream(path, FileMode.Open,
            FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.SequentialScan | FileOptions.Asynchronous);
        string actual = Convert.ToHexString(
            await SHA256.HashDataAsync(read, token));
        return actual.Equals(exactSha, StringComparison.OrdinalIgnoreCase);
    }

    private static void Guard(string path)
    {
        string? here = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(here))
        {
            if ((Directory.Exists(here) || File.Exists(here)) &&
                (File.GetAttributes(here) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("M195_TOOL_REPARSE_PATH_REFUSED");
            string? parent = Path.GetDirectoryName(here);
            if (string.Equals(parent, here, StringComparison.OrdinalIgnoreCase))
                break;
            here = parent;
        }
    }
}
