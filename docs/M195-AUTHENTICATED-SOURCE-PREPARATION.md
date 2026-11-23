# M195: verified official AMD EXE → unsigned candidate (developer workflow)

**Status:** Source and offline tests exist on the M195 draft branch. This is **NOT the finished Finesse GUI**, a signed package, an installer, or physical Legion Go validation.

A new `OfficialAmd26111Downloader.DownloadFreshAsync` component can also acquire the **exact pinned official AMD 26.11.1 EXE** over HTTPS without manually locating the EXE. It uses the frozen M121 official-host allowlist (only `drivers.amd.com` / `download.amd.com`), restricted redirects and AMD referer retry, streaming size/SHA checks, then full Windows WinVerifyTrust publisher validation. It does not execute the EXE. Any different filename, hash, size, host, or redirect is rejected; no unofficial mirrors are used.

The standalone CLI below still accepts an existing original AMD EXE path. The new downloader is a reusable code component for the eventual GUI; **a real network acquisition with this new implementation has not been executed as part of public GitHub CI**.

The preparation engine now has two source-first phases:

1. `OfficialAmdInstallerVerifier.Verify26111Async`: verifies the **exact** official AMD 26.11.1 EXE filename, byte length, SHA-256 and actual Windows WinVerifyTrust Authenticode signer, rehashing the source afterward.
2. `Amd26111ArchiveExtraction.ExtractAsync`: verifies the frozen 7zr 26.04 executable by exact byte length/SHA-256, then selects exactly **195 original archive paths** (194 AMD display files plus the original `u0206030.cat`). It accepts extractor exit code 0, or exit 1 **only if every original file and its SHA-256 passes**. All output is isolated.
3. `Amd26111TwoPhasePreparation.VerifyAndStageAsync`: verifies the AMD EXE again, validates exact hardware profile, the public 194-path SHA-256 manifest, required original INF/DAT SHA-256 and authorized profile policy, then rebuilds unsigned INF/DAT and stages a verified 194-file candidate. Original AMD catalog remains separate for **later** Windows signature and dual-trust validation.

The standalone tool `Source/tools/Finesse.OfficialSourcePreparation/` calls these boundaries in order and emits evidence. Nothing modifies the driver store, Windows certificates, GPU bindings, AMD Software, or boot state.

## Build the developer CLI

Install the normal **system-wide .NET 10 SDK** with `dotnet` available on Windows `PATH`. No project-local or portable .NET SDK.

```powershell
dotnet --version
dotnet build Source/tools/Finesse.OfficialSourcePreparation/Finesse.OfficialSourcePreparation.csproj -c Release
```

The 7zr extraction helper is separate from .NET: it must match **exactly** the frozen 26.04 reference `7zr.exe` (602,624 bytes; SHA-256 `256FECA8E274E5DA655E2A284FABAFD9F554365EB164862089DACD4E8276D282`). Do not substitute arbitrary executables or remove the SHA-256 check.

## Run (development only)

The authentic AMD EXE must be the exact frozen 26.11.1 package. The `--lenovo-policy` path requires the properly sourced, exact profile registry directives—**those directives are not currently distributed through this repository**.

```powershell
dotnet run --project Source/tools/Finesse.OfficialSourcePreparation/Finesse.OfficialSourcePreparation.csproj -c Release -- `
  --installer "C:\PATH\TO\amd-software-adrenalin-edition-26.11.1-win11-c.exe" `
  --extractor "C:\PATH\TO\VERIFIED\7zr.exe" `
  --manifest "Source/contracts/releases/amd-26.11.1-original-194.sha256" `
  --lenovo-policy "C:\PATH\TO\AUTHORIZED\Go1-policy-rows.txt" `
  --profile Go1 `
  --output-root "C:\PATH\TO\NEW\M195-Unsigned-Source-Verification"
```

Supported `--profile`: `Go1`, `GoS`, `Go2`. The `--output-root` must not exist. The tool uses isolated subfolders for verified original extraction and unsigned candidate. It writes evidence files and hashes, not an installable Windows driver.

### Optional pinned Microsoft Inf2Cat (still unsigned)

The offline `Finesse.OfficialSourcePreparation` CLI also accepts an optional `--inf2cat "C:\\PATH\\TO\\VERIFIED\\Inf2Cat.exe"` argument. It must match the frozen WDK tool SHA-256 `82C302FC9069783674B51665CE80E769F8500DB7FC737A4B8A3773C521950B86`.

When supplied, the tool rechecks the **entire 194-file original/candidate hash closure**, runs Inf2Cat, verifies that the generated catalog contains **zero signer infos**, records the tool logs, and confirms the 194 files and separate original AMD catalog were not modified.

This is a **generated unsigned catalog**, not a trusted, signed, staged or installable Windows package. The Inf2Cat stage has compiled and passed rejection tests in CI, but has **not** been run with the real Microsoft WDK executable and authentic AMD package in M195.

**Current validation limits:** Windows CI compiles this CLI and runs synthetic negative cases (including the official AMD download URL/redirect allowlist), device-routing cases, MetroHash/DAT transforms, INF cloning, the public 194-line manifest, 195-file original extraction-selection checks, and the unsigned 194-file staging tests. **CI does not download or execute the real 1.8 GB AMD installer**, and has not executed 7zr against that package. Real-source end-to-end extraction and physical postboot Code 0 must be measured separately.

## Remaining GUI/source work

The frozen GUI `M182InstallLifecycleWorkflow.RunAsync` calls `M80Recipe.LoadForM194` before acquiring AMD source. It must instead use the source-only contract, authenticated download/EXE verification and 195-file extraction first, then the public candidate recipe and staging. The remaining signed-catalog generation, exact 14/14 dual-original/local trust, recovery backup, scoped cleanup, AMD Software installation **before the one planned reboot**, postboot observation and rollback must retain their M194 behaviors.

The full GUI/lifecycle source remains unpublished and the GitHub full-installer build is not yet verified. **Do not feed unsigned output into `pnputil`.** Frozen M194 is unmodified.
