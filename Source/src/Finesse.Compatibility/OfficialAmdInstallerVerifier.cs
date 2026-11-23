using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Finesse.Compatibility;

/// <summary>
/// Read-only Windows verification of the exact AMD 26.11.1 installer identity.
/// No downloads, execution, extraction, registry writes, certificate changes,
/// test-signing, driver installation, or reboot.
/// The original Windows WinVerifyTrust code path follows frozen M194.
/// </summary>
public static class OfficialAmdInstallerVerifier
{
    private static readonly Guid GenericVerifyV2 =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public sealed record VerifiedSource(
        string FullPath,
        string Sha256,
        long Bytes,
        string SignerSubject,
        string SignerThumbprint,
        DateTimeOffset VerifiedAtUtc);

    public static async Task<VerifiedSource> Verify26111Async(
        string installerPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Authenticode requires Windows.");
        string full = Path.GetFullPath(installerPath);
        RejectReparseAncestors(full);
        var contract = OfficialAmd26111SourceContract.VerifiedFrozenM194;
        if (!string.Equals(Path.GetFileName(full), contract.InstallerFileName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AMD_SOURCE_FILENAME_NOT_PINNED");
        if (!File.Exists(full))
            throw new FileNotFoundException("Pinned AMD installer is absent.", full);
        var info = new FileInfo(full);
        if (info.Length != contract.ExactInstallerBytes)
            throw new InvalidDataException("AMD_SOURCE_LENGTH_NOT_PINNED");
        string firstHash = await HashFileAsync(full, cancellationToken);
        if (!string.Equals(firstHash, contract.InstallerSha256, StringComparison.Ordinal))
            throw new InvalidDataException("AMD_SOURCE_SHA256_NOT_PINNED");

        // WinVerifyTrust checks Windows certificate chain/policy. The SHA256
        // pin prevents substituting a different signed executable or publisher.
        var result = GetWindowsAuthenticode(full, cancellationToken);
        if (result.Status != 0 ||
            !result.Subject.Contains(contract.ExpectedSignerName,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(result.Thumbprint))
            throw new InvalidDataException("AMD_SOURCE_WINVERIFYTRUST_OR_SIGNER_FAILED");

        // Recheck after the signature operation. The later extraction stage
        // separately validates every original file against a frozen inventory.
        RejectReparseAncestors(full);
        if (!File.Exists(full) || new FileInfo(full).Length != contract.ExactInstallerBytes ||
            await HashFileAsync(full, cancellationToken) != contract.InstallerSha256)
            throw new InvalidDataException("AMD_SOURCE_CHANGED_DURING_TRUST_VERIFICATION");

        return new VerifiedSource(full, contract.InstallerSha256,
            contract.ExactInstallerBytes, result.Subject, result.Thumbprint,
            DateTimeOffset.UtcNow);
    }

    private static async Task<string> HashFileAsync(
        string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static (int Status, string Subject, string Thumbprint)
        GetWindowsAuthenticode(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IntPtr filePointer = IntPtr.Zero;
        try
        {
            var file = new WinTrustFileInfo(path);
            filePointer = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(file, filePointer, false);
            var data = new WinTrustData(filePointer);
            int status = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data);
            if (status != 0)
                return (status, string.Empty, string.Empty);

            // .NET's older API extracts the PE signer identity; WinVerifyTrust
            // status remains the Windows authenticity decision.
#pragma warning disable SYSLIB0057
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return (status, signer.Subject, signer.GetCertHashString().ToUpperInvariant());
        }
        catch (CryptographicException)
        {
            // A missing/unreadable signer identity must fail the caller's gate.
            return (unchecked((int)0x800B0100), string.Empty, string.Empty);
        }
        finally
        {
            if (filePointer != IntPtr.Zero)
                Marshal.FreeHGlobal(filePointer);
        }
    }

    private static void RejectReparseAncestors(string path)
    {
        string? cursor = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("AMD_SOURCE_REPARSE_POINT_REJECTED");
            string? parent = Path.GetDirectoryName(cursor);
            if (string.Equals(cursor, parent, StringComparison.OrdinalIgnoreCase))
                break;
            cursor = parent;
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [In] Guid pgActionID,
        ref WinTrustData pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;

        public WinTrustFileInfo(string path)
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            pcwszFilePath = path;
            hFile = IntPtr.Zero;
            pgKnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public string? pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;

        public WinTrustData(IntPtr file)
        {
            cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
            pPolicyCallbackData = IntPtr.Zero;
            pSIPClientData = IntPtr.Zero;
            dwUIChoice = 2; // WTD_UI_NONE
            fdwRevocationChecks = 0;
            dwUnionChoice = 1; // WTD_CHOICE_FILE
            pFile = file;
            dwStateAction = 0;
            hWVTStateData = IntPtr.Zero;
            pwszURLReference = null;
            dwProvFlags = 0x00000100; // M194 original: revocation check disabled
            dwUIContext = 0;
            pSignatureSettings = IntPtr.Zero;
        }
    }
}
