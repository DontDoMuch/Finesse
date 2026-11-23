# Building Finesse from source

> **Status: public source build is not available yet.** This is the documented M195 target, not a claim that the current GitHub repository can already produce the installer. The frozen M194 source remains a separate private-lab baseline.

## Intended requirements

- Windows 11 x64 build machine
- **System-installed Microsoft .NET 10 SDK**, installed normally and made available as `dotnet` on the machine's `PATH`
- PowerShell 5.1 or PowerShell 7 for the build script; all script features must work on the stated minimum version
- Network access for authenticated restore of declared dependencies
- Windows SDK tooling as actually required by the source and its test harnesses

**Do not use or download a project-local/portable SDK.** No `dotnet-install.ps1`, unpacked `dotnet.exe` shipped with the repo, private portable SDK cache, machine-specific path, or implicit SDK installation in `build.ps1`. If the global .NET 10 SDK is missing, report the requirement and stop. GitHub Actions must use an installed SDK on its Windows runner (it may install the SDK through a pinned, reviewed setup step), not a project-local runtime folder.

This is a **developer build dependency only**. The published Finesse EXE remains self-contained; end users do **not** install .NET to run it.

## Planned source-build commands

Once the source-publication blockers below are resolved, the supported instructions will be:

```powershell
git clone https://github.com/DontDoMuch/Finesse.git
Set-Location Finesse
dotnet --info
.\build.ps1
```

The M195 branch now includes `build.ps1`, which resolves the **system-installed** CLI with `Get-Command dotnet -CommandType Application -ErrorAction Stop`, use `$PSScriptRoot` for repository-relative paths, verify `dotnet --version` and its exit code against the SDK requirement, run restore/build/tests, publish a **self-contained Windows x64 single-file EXE**, check every native process exit code, and produce a SHA-256 manifest. CI must run **the same script**.

The M195 branch also includes `global.json` selecting an installed .NET 10 SDK with `latestFeature` roll-forward. **It does not install .NET, define an environment variable, or specify an absolute filesystem path.** System-wide discovery comes from `PATH`, not from manually manipulating `DOTNET_ROOT` or assuming the maintainer's SDK directory.

**Current behavior:** The script intentionally fails with `M195_PUBLIC_SOURCE_NOT_PUBLISHED` while the full public source is absent. The Windows CI workflow calls the same script and cannot legitimately succeed or publish an executable until that blocker and the private-resource guards are resolved. The presence of a workflow file alone is not CI build proof. See [source audit](docs/M195-SOURCE-AUDIT.md).

## Available now: independently buildable compatibility core

Although the **full installer is not yet publicly buildable**, M195 includes an independently compilable, vendor-binary-free compatibility library and offline DAT verifier. These use your **globally installed .NET 10 SDK**, not a portable SDK.

```powershell
dotnet run --project Source/test/Finesse.Compatibility.OfflineTests/Finesse.Compatibility.OfflineTests.csproj -c Release
dotnet build Source/tools/Finesse.DatVerifier/Finesse.DatVerifier.csproj -c Release
```

The tests cover all three exact hardware profiles, release-scoped CopyINF closure, positional DAT parsing/encoding, the recovered release-specific MetroHash64, checksum/tamper rejection, and full synthetic DAT rebuilds.
The compatibility core now also contains `AmdInfSourceRebuilder` and the combined `Amd26111CandidateRecipe`, which reconstruct the INF directly from an independently verified original AMD INF **without embedding proprietary vendor files**. Exact source and final hashes are mandatory for the known AMD 26.11.1 release.

```powershell
dotnet build Source/tools/Finesse.CandidateVerifier/Finesse.CandidateVerifier.csproj -c Release
```

For a source-driven, strictly offline INF+DAT candidate reproduction using your own authentic AMD source and authorized Lenovo policy data, see [INF and DAT integration](docs/M195-INF-DAT-INTEGRATION.md). This is a developer verification tool, **not** a driver installer.

### Source-first 194-file staging (offline)

M195 also includes a source-first stager that checks an exact original 194-file AMD inventory, replaces the source-derived INF/DAT, re-verifies the complete candidate package, and stores the original main AMD catalog separately. This is **still unsigned and not installable**.

```powershell
dotnet build Source/tools/Finesse.SourceFirstVerifier/Finesse.SourceFirstVerifier.csproj -c Release
```

It requires an authentic original AMD extraction and authorized profile policy. The 194-file **filename/hash manifest is now public** at [Source/contracts/releases/amd-26.11.1-original-194.sha256](Source/contracts/releases/amd-26.11.1-original-194.sha256); the production GUI wiring remains incomplete. See [source-first staging](docs/M195-SOURCE-FIRST-STAGING.md). Both build/test commands run in a separate **offline compatibility core** CI job. That job passing proves compilation and those tests—not a complete GUI installer build or successful GPU installation.

With the original AMD source DAT legitimately extracted, independent reviewers can reproduce the exact Go 1/Go S/Go 2 SHA-256 digests. See [offline DAT verification](docs/M195-OFFLINE-DAT-VERIFICATION.md).

### Authenticated official AMD source preparation (developer tool)

The M195 compatibility core now contains original AMD EXE verification, source-selective 195-file extraction, and unsigned 194-file staging, with a separately pinned extractor binary and an exact-source/target SHA-256 contract. It also includes an approved-host HTTPS downloader intended for eventual GUI integration.

```powershell
dotnet build Source/tools/Finesse.OfficialSourcePreparation/Finesse.OfficialSourcePreparation.csproj -c Release
```

Read [authenticated source preparation](docs/M195-AUTHENTICATED-SOURCE-PREPARATION.md) for expected inputs, the approved AMD executable/extractor, limits and the complete developer CLI command. **This is not yet the finished Finesse installer and it does not sign or install drivers.** No portable .NET SDK is needed.

## Blockers before a genuine public build

1. **Source completeness:** The current repository does not yet include the full compilable Finesse source.
2. **Private lab guard:** The verified M194 GUI installation path currently depends on `FINESSE_PRIVATE_LAB`; it must be refactored so normal public builds are functional without enabling a private test switch.
3. **AMD-derived private resources:** The M194 engineering source embeds AMD-derived INF/DAT/package artifacts. Do **not** publish those assets without permission. Replace them with lawful acquisition from the official AMD installer plus deterministic local reconstruction and verification, while preserving the proven compatibility behavior.
4. **Regression and hardware tests:** Publish all distributable test code, run real Windows CI, and track separately which devices have physically passed the post-reboot acceptance check.
5. **Trust and recovery:** Preserve the M194 signing, dual-trust validation, backup, cleanup, software installation, one-planned-reboot, first postboot observation, and evidence-based recovery behavior.

## Required build behavior

- Pin and verify dependencies, selected system SDK and tool versions; avoid hardcoded developer paths or project-local SDK downloads
- Detect the exact supported hardware profile (Go 1 Z1 Extreme, Go S Z1 Extreme, Go 2 Z2 Extreme)
- Preserve the profile-specific AMD-faithful INF and DAT reconstruction, including **dynamic 3-byte positional DAT encoding and release-specific MetroHash64**, not copied 26.11.1 offsets/hashes
- Treat a successful compiler exit as **build success only**, not driver compatibility proof
- Do not commit downloaded AMD driver binaries, signing private keys, sensitive diagnostic archives, or user's personal paths
- Never claim a source-built EXE matches an official Release binary without comparing actual hashes and provenance

For developer contributions, see [CONTRIBUTING.md](CONTRIBUTING.md). For current verification claims, see [VERIFYING.md](VERIFYING.md).
