# Building Finesse from source

> **Status: public source build is not available yet.** This is the documented M195 target, not a claim that the current GitHub repository can already produce the installer. The frozen M194 source remains a separate private-lab baseline.

## Intended requirements

- Windows 11 x64 build machine
- .NET **10 SDK** installed through the normal Microsoft installer (available as `dotnet` on `PATH`)
- PowerShell 5.1 or PowerShell 7 for the build script; all script features must work on the stated minimum version
- Network access for authenticated restore of declared dependencies
- Windows SDK tooling as actually required by the source and its test harnesses

You will **not** need the maintainer's machine-specific .NET directory, an undocumented portable SDK cache, Test Signing, or a private certificate to compile the application.

## Planned source-build commands

Once the source-publication blockers below are resolved, the supported instructions will be:

```powershell
git clone https://github.com/DontDoMuch/Finesse.git
Set-Location Finesse
.\build.ps1
```

The future `build.ps1` must discover `dotnet` with `Get-Command dotnet -CommandType Application`, use `$PSScriptRoot` for repository-relative paths, run restore/build/tests, publish a **self-contained Windows x64 single-file EXE**, check each native process exit code, and produce a SHA-256 manifest. CI must run **the same script**.

A repository `global.json` will select .NET 10 with a declared SDK roll-forward policy. It is *not* an absolute SDK path or a substitute for installing the SDK.

## Blockers before a genuine public build

1. **Source completeness:** The current repository does not yet include the full compilable Finesse source.
2. **Private lab guard:** The verified M194 GUI installation path currently depends on `FINESSE_PRIVATE_LAB`; it must be refactored so normal public builds are functional without enabling a private test switch.
3. **AMD-derived private resources:** The M194 engineering source embeds AMD-derived INF/DAT/package artifacts. Do **not** publish those assets without permission. Replace them with lawful acquisition from the official AMD installer plus deterministic local reconstruction and verification, while preserving the proven compatibility behavior.
4. **Regression and hardware tests:** Publish all distributable test code, run real Windows CI, and track separately which devices have physically passed the post-reboot acceptance check.
5. **Trust and recovery:** Preserve the M194 signing, dual-trust validation, backup, cleanup, software installation, one-planned-reboot, first postboot observation, and evidence-based recovery behavior.

## Required build behavior

- Pin and verify dependencies, selected SDK and tool versions; avoid hardcoded developer paths
- Detect the exact supported hardware profile (Go 1 Z1 Extreme, Go S Z1 Extreme, Go 2 Z2 Extreme)
- Preserve the profile-specific AMD-faithful INF and DAT reconstruction, including **dynamic 3-byte positional DAT encoding and release-specific MetroHash64**, not copied 26.11.1 offsets/hashes
- Treat a successful compiler exit as **build success only**, not driver compatibility proof
- Do not commit downloaded AMD driver binaries, signing private keys, sensitive diagnostic archives, or user's personal paths
- Never claim a source-built EXE matches an official Release binary without comparing actual hashes and provenance

For developer contributions, see [CONTRIBUTING.md](CONTRIBUTING.md). For current verification claims, see [VERIFYING.md](VERIFYING.md).
