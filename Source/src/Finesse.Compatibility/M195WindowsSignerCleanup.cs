using System.Security.Cryptography.X509Certificates;

namespace Finesse.Compatibility;

/// <summary>
/// Direct port of M194 M179SignerCleanup: only an explicitly owned, newly
/// created Finesse installation signer may be removed, and ONLY after Windows
/// has confirmed that staging did not succeed. Never purge old/active trust.
/// </summary>
internal static class M195WindowsSignerCleanup
{
    internal static void RemoveUnstagedSigner(
        M195LiveCatalogSigner.Signed signer, bool stageApiReportedSuccess)
    {
        ArgumentNullException.ThrowIfNull(signer);
        if (stageApiReportedSuccess)
            throw new InvalidOperationException(
                "M195_SIGN_CANNOT_REMOVE_TRUST_FOR_STAGED_PACKAGE");
        if (!M195LiveSigningPreflight.IsOwnedSigner(
                signer.SignerThumbprint, signer.SignerSubject))
            throw new InvalidDataException("M195_SIGN_CLEANUP_REQUIRES_OWNED_IDENTITY");
        foreach (StoreName name in new[] {
            StoreName.TrustedPublisher, StoreName.Root, StoreName.My })
        {
            using var store = new X509Store(name, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            X509Certificate2Collection found =
                store.Certificates.Find(X509FindType.FindByThumbprint,
                    signer.SignerThumbprint, validOnly: false);
            foreach (X509Certificate2 item in found)
            {
                if (!string.Equals(item.Subject, signer.SignerSubject,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "M195_SIGN_CLEANUP_SUBJECT_DIFFERED_FROM_RECEIPT");
                store.Remove(item);
            }
        }
    }
}
