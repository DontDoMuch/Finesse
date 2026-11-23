# M195 — Source-driven INF + DAT candidate integration

## Verified frozen M194 baseline

This work is derived from the original M194 source and the locally preserved official AMD INF from the M188 comparison evidence. It does **not** change, recompile, upload or overwrite frozen M194.

The original authentic AMD **26.11.1** INF has SHA-256:

```text
2E1125EB79359D3BAEB283B81868064257C5F1436D8928AC2EC123837DD70C57
```

The original AMD DAT has SHA-256:

```text
2DC60D0B41488E504E78213B8DA1DF790E7E39AF67A892E9441F41642659E459
```

The M194 INF differs from the unmodified AMD source through exactly **two insertion regions** per profile: one exact PCI device mapping in the AMD manufacturer section and the appended profile-specific sections. The original AMD sections are cloned **at runtime from the caller's verified AMD source**, not stored in this repository. The 26/30/28 Lenovo policy directives are provided externally. Then only the two observed, absent-companion `CopyINF` directives are removed (11 occurrences of each in this verified release, never an unconditional rule for later AMD releases).

| Profile | M194 section | Before CopyINF removal SHA-256 | After CopyINF removal SHA-256 |
|---|---|---|---|
| Go 1 Z1 Extreme | `ati2mtag_Phoenix_LegionGo` | `A8C953394300180E33AC5D96611CCC0EBFBCC16C6D18618598ACBC237C1CDE65` | `5F49F240CBDA41F7BD6E30867751507ACF38386B7AA3E0848639BCD002FE691A` |
| Go S Z1 Extreme | `ati2mtag_Phoenix_LegionGoS` | `FBF807952823DF6F87529841E3804C5AA5EA9B6C10E3E1EB303E9EA52E0539D8` | `E2B0809F3E3D135BFD03F1E0D97D1140BD6A05557024CE1D3EB604DFDAA90071` |
| Go 2 Z2 Extreme | `ati2mtag_Strix_LegionGo2` | `5DF3130B11CB52F87783CCA7B0895DEBA8871DFCDE9550A2F246B6EA14D266E4` | `A355187F07FEEE5A0E0EDCC772A95C68E4F54B04323DE3D45634FFBE447CCBA3` |

The locally reconstructed pre-cleanup INF files matched the three frozen M194 modified INF resources **byte-for-byte**. Their cleaned digests were independently confirmed from the same frozen bytes. GitHub-hosted CI has separately compiled and tested the source-driven algorithm using synthetic, vendor-free fixtures.

The same exact 26.11.1 release marker is used to calculate the DAT MetroHash64 header:

```text
26.20.15.16-261109a-206030E-AMD-Software-Adrenalin-Edition
```

The expected rebuilt DAT SHA-256 is `7986B5DC0CB3C2207ECE857D0060475A603B5342649D146874850C0D4A2509CA` for Go 1/Go S, and `B63E4F58C164950B41C07071B3CF6797B35ACC597B9476836FCA66E8A658E3FD` for Go 2.

## Implementation now in the public branch

- `Source/src/Finesse.Compatibility/AmdInfSourceRebuilder.cs`: clones the original Phoenix/Strix install sections, adds only the exact device mapping and provided policy, verifies the frozen source and candidate SHA-256, then closes verified `CopyINF` references.
- `Source/src/Finesse.Compatibility/AmdDatReleaseRebuilder.cs`: parses the original 12-byte-header/3-byte positional schema, inserts exact device/revision records, calculates the original release-specific MetroHash64 header, and verifies round-trip results.
- `Source/src/Finesse.Compatibility/Amd26111CandidateRecipe.cs`: single **pure in-memory** entry point combining the two hash-pinned transformations, returning **unsigned** INF/DAT bytes.
- `Source/src/Finesse.Compatibility/OfficialAmd26111SourceContract.cs`: the **source-first** contract now holds the exact official AMD 26.11.1 installer filename, byte count, SHA-256, expected AMD signer, driver version, original INF/DAT/kernel digests, catalog location, and 194-file selection count. It deliberately contains no AMD package payload. This gives the GUI's downloader the identity metadata it needs **before** calculating a device-specific recipe.
- `Source/tools/Finesse.CandidateVerifier`: a CLI that takes caller-supplied source INF, DAT and externally provided registry policy, verifies every input and output hash, then writes a new isolated folder with the two files and a SHA-256 manifest.

**Source only, not a release binary.** There are no AMD/Lenovo complete INF files, DAT files, or certificates committed to the repository. The registry-policy input is intentionally not bundled pending rights/provenance review. This means the CLI requires legitimately obtained profile policy data and is not a ready-to-use end-user installer.

### Offline developer command

Use the **system-installed .NET 10 SDK** discoverable on `PATH`:

```powershell
dotnet run --project Source/tools/Finesse.CandidateVerifier/Finesse.CandidateVerifier.csproj -c Release -- `
  --original-inf "C:\PATH\TO\OFFICIAL\u0206030.inf" `
  --original-dat "C:\PATH\TO\OFFICIAL\B026778\amdgcf.dat" `
  --lenovo-policy "C:\PATH\TO\AUTHORIZED\Go1-policy-rows.txt" `
  --profile Go1 `
  --output-dir ".\local-candidate-Go1"
```

Valid `--profile` values are `Go1`, `GoS`, and `Go2`. Each output folder must be new; the tool refuses overwrite and performs **no driver installation, certificate operations or restart**.

## Required integration into the real GUI

The frozen `M182InstallLifecycleWorkflow.RunAsync` currently calls `M80Recipe.LoadForM194(profile)` **before** AMD installer acquisition. That loads private embedded INF/DAT/policy resources and the M79 receipt/inventory. The `M80Pipeline.PrepareAsync` interface also assumes the full private recipe is already materialized before extraction.

The correct M195 refactor is therefore explicitly **two-stage**:

1. Detect the exact supported Lenovo hardware profile and backup the assigned working driver (existing lifecycle).
2. Load a **public source-only** 26.11.1 release contract (official installer SHA-256, filename/size, known selection/inventory paths), rather than embedded modified INF/DAT.
3. Acquire the authentic AMD installer with existing signature and SHA-256 validation, and extract original INF, DAT, binaries and Microsoft-signed catalog into an isolated working directory.
4. Derive and verify authorized profile registry directives; feed these original bytes into `Amd26111CandidateRecipe.Build` and require both pinned output hashes **before any installation**.
5. Update the 194-file prepared candidate inventory with the new INF/DAT hashes and verify unchanged original AMD kernel/user binaries against the authenticated package. Regenerate the candidate catalog; **never reuse the old main catalog**.
6. Continue the established M191/M192 dual-catalog trust (14/14), scoped cleanup, signing, GPU bind, AMD Software-before-restart, one planned reboot, first postboot verification and rollback. Maintain the existing UI's simple 12-step progress and detailed background receipts.

**Not yet implemented:** the GUI two-stage extraction handoff, public source inventory manifest, license-cleared registry-policy acquisition, and reproducible signature/catalog lifecycle. The public-source installer build must continue failing until these exist.

## Evidence status

- **Local frozen-source differential:** exact original-to-profile INF transformation identified and all three frozen pre/post hashes matched.
- **GitHub CI:** synthetic source-driven INF and DAT compatibility tests passed; separate candidate CLI compiled on Windows.
- **Physical driver:** M193 Go 1 and M194 Go 2 postboot Code 0 evidence precedes M195. M195 code **has not been installed on physical hardware**.
- **Full public GUI:** still blocked and no attested release exists.

No claim of compatibility is made for later AMD releases until their actual originals, layout, release marker, INF directives, and signing paths are inspected anew.
