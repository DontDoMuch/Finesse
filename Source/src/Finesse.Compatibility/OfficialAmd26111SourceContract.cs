namespace Finesse.Compatibility;

/// <summary>
/// Vendor-owned AMD installer data is intentionally NOT embedded. This
/// public immutable identity is enough for the existing M121 downloader to
/// authenticate/locate a source BEFORE a profile recipe exists.
/// WinVerifyTrust signer checks still belong to the Windows acquisition layer.
/// </summary>
public sealed record OfficialAmd26111SourceContract(
    string InstallerFileName,
    long ExactInstallerBytes,
    string InstallerSha256,
    string ExpectedSignerName,
    string DriverVersion,
    string RootInInstallerArchive,
    string OriginalInfRelative,
    string OriginalInfSha256,
    string OriginalDatRelative,
    string OriginalDatSha256,
    string OriginalKernelRelative,
    string OriginalKernelSha256,
    string OriginalMainCatalogRelative,
    int SelectedFileCount)
{
    public static OfficialAmd26111SourceContract VerifiedFrozenM194 { get; } = new(
        "amd-software-adrenalin-edition-26.11.1-win11-c.exe",
        1_780_861_240,
        "5A2B7A334D3E91E0B278F45CAD981CE5C0DFE610311DC7E5F2CA0D6D87119211",
        "Advanced Micro Devices",
        "32.0.32015.16001",
        "Packages/Drivers/Display/WT6A_INF",
        "u0206030.inf",
        "2E1125EB79359D3BAEB283B81868064257C5F1436D8928AC2EC123837DD70C57",
        "B026778/amdgcf.dat",
        "2DC60D0B41488E504E78213B8DA1DF790E7E39AF67A892E9441F41642659E459",
        "B026778/amdkmdag.sys",
        "34B18AB97C75010F91128F5B802C9D8A3CFC6B9DAE586A8B42FD4A00738EB04D",
        "u0206030.cat",
        194);
}
