# M195 public-source blocker audit — from frozen M194

**Scope:** Audit of the actual frozen `M194-26111-THREE-DEVICE-FULL-LIFECYCLE-SOURCE.zip`, not a claim that the files have been published on GitHub.

Frozen archive SHA-256: `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`.

## What was confirmed

The M194 source archive contains **483 ZIP members**, including its checksum manifest and a `Source/` development tree. The source contains a WinForms `Finesse.Gui` project, device-specific C# engines, harnesses, private recipe files and external source contracts.

The following items block genuine public source builds:

| Actual source path / behavior | Required change |
|---|---|
| `Source/src/Finesse.Gui/PrivateM80/Assets/M79-26111-PRIVATE-RECIPE-INPUT.zip` (4,823,062 bytes) | This archive contains AMD-derived INF/DAT/catalog recipe material. Do **not** commit or embed; replace with deterministic per-release preparation from authenticated AMD official source |
| `.../Assets/M147/GoS-u0206030.inf` (1,123,417 bytes) | Derive Go S Phoenix INF from verified AMD installer instead of shipping this modified proprietary INF |
| `.../Assets/M147/Go2-u0206030.inf` (1,122,948 bytes) | Derive Go 2 Strix INF from verified AMD installer instead of shipping this modified proprietary INF |
| `.../Assets/M147/*.dat`, `M193-Go1-amdgcf.dat`, `M194-GoS-amdgcf.dat`, `M194-Go2-amdgcf.dat` | Implement generic inspected 12-byte-header/3-byte-position-record DAT decoding/reconstruction with release-specific MetroHash64, not embedded release-specific binary blobs |
| `.../Assets/LegionGo-AMD-26.9.2-Public-Beta-v5.0-ORIGINAL.zip` | Audit embedded vendor data and redistribution rights; do not upload this opaque archive without clearance |
| `Source/src/Finesse.Gui/Finesse.Gui.csproj` | Remove proprietary `EmbeddedResource` entries and any private build-time dependencies; keep allowed implementation scripts and verified contracts if owned |
| `Source/src/Finesse.Gui/PrivateM80/M80Recipe.cs` | Refactor `LoadForM194` and embedded-resource loaders into validated source-driven recipe construction; all profile-specific checks remain |
| `Source/src/Finesse.Gui/Program.cs` | Remove `FINESSE_PRIVATE_LAB` gating from the *normal* installer entry point; preserve privileged developer-only features behind separate explicit controls |
| `Source/test/M194.MultiProfileHarness/Program.cs` | Replace tests requiring proprietary embedded DAT files with legal fixture generation and authoritative private/offline validation; preserve 40 regression requirements |

## Build gate now in the draft branch

The `build.ps1` added under M195 uses the **normal system-installed .NET 10 SDK** from `PATH`, checks source completeness, refuses the old private source recipe and private-gated UI, runs the release-specific tests and produces a self-contained EXE only after those checks pass.

The Windows Actions workflow calls this same script. **Until the full public source exists, an actual CI build must fail** with a source-incomplete error. No successful installation, artifact provenance, or user-ready release is being claimed.

## Requirements that must not be lost in refactoring

- Exact Legion Go 1, Go S Z1 Extreme, and Go 2 Z2 Extreme hardware detection; fail for unverified models
- Release-specific AMD Phoenix or Strix INF cloning and exact CopyINF closure derived from original verified files
- Release-specific `amdgcf.dat` decode/insert/encode/re-MetroHash with complete round trip and unchanged unrelated records
- Original AMD kernel/user binaries unchanged; backup of active working GPU package before any mutation
- Scoped silent cleanup, one live signing stage, original AMD catalog alongside Finesse catalog, staged Driver Store 14/14 dual coverage and signing verification
- AMD Software installation and targeted MSI version-conflict handling before one planned reboot
- First postboot GPU problem code, INF and driver identity captured **before** recovery
- Exact hardware-baseline rollback and report integrity

## License review

This repository currently uses the PolyForm Noncommercial License 1.0.0. That is source-available but not an OSI-recognized open-source license. A change requires the owner's explicit decision, and does not grant rights to redistribute third-party AMD/Lenovo files.

**Freeze rule:** Do not modify frozen M194. M195+ must branch from it conceptually and maintain an independently reviewed source and binary evidence trail.
