# M195 original-source → unsigned 194-file staging

**Development status:** Windows CI compiles and tests this offline staging boundary. It is not a GUI installer or a claim of successful driver installation on hardware.

## What now exists

The following redistributable C# sources contain **no AMD binaries**:

- `Amd26111SourceInventory.cs` — verifies an exact 194-file original AMD display inventory from relative filenames and SHA-256 hashes. Requires an independently pinned **canonical manifest identity**.
- `AmdSourceFirstStager.cs` — validates every original file **before writing**, creates an isolated candidate package, replaces only the profile-specific INF and DAT, verifies all 194 output hashes, and preserves the original official AMD main catalog **outside** the unsigned candidate package.
- `Amd26111CandidateRecipe.cs` — reconstructs INF and DAT from caller-supplied original AMD files and externally verified Lenovo registry-policy rows; all three 26.11.1 candidates must match the frozen SHA-256 values.
- `Source/tools/Finesse.SourceFirstVerifier/` — standalone CLI joining source verification, profile candidate construction, and offline staging.

No Microsoft/AMD trust registration, catalog generation/signing, `pnputil`, reboot, driver overwrite, AMD Software install, or device change occurs.

## Exact original and candidate manifest identities

These fingerprints were independently calculated from the **actual frozen M194 194-file inventory**, replacing its private modified Go 1 INF hash with the **original** authenticated AMD INF SHA-256. Canonical representation is:

```text
<UPPERCASE-SHA256>  <relative/path>
```

Sort rows by relative path using a case-insensitive ordinal comparison, encode ASCII, use **LF** line endings, and include a trailing LF after the last row. Paths retain their exact case.

| Manifest | SHA-256 |
|---|---|
| Original AMD 26.11.1 194 files | `C7757CFE6D743D7A6AAC33292753FEB08811CA898A43BF60B724481E24042D08` |
| Rebuilt Go 1 candidate | `6F2533085DF9656AE168AF7D95D490E333B338D1D9181DD3C8B32A24A90A7944` |
| Rebuilt Go S Z1 Extreme candidate | `D81432A1469DC04FFCCAE072EBE0E3B064A6DFF9E9949EFF92FA035ED8A49F1B` |
| Rebuilt Go 2 Z2 Extreme candidate | `B5F1C78B9B929ADCE7C5BDDD54CD3A2FA03911A2117159E63266D67CA7B7E369` |

The manifest contains only file paths and hashes, not vendor content. **The full public selection/manifest has not yet been published**; until then, the pinned source-first CLI is available for engineering validation with the original authoritative manifest, not yet a self-contained third-party build workflow. Supplying a forged or incomplete manifest will fail the source inventory gate.

## System-installed .NET 10 compilation

```powershell
dotnet --info
dotnet run --project Source/test/Finesse.Compatibility.OfflineTests/Finesse.Compatibility.OfflineTests.csproj -c Release
dotnet build Source/tools/Finesse.SourceFirstVerifier/Finesse.SourceFirstVerifier.csproj -c Release
```

No portable SDK, installer self-download, or user-specific SDK path is used.

### Offline CLI (engineering inputs required)

These arguments must refer to files you have lawfully acquired and independently authenticated. The display root should be `Packages/Drivers/Display/WT6A_INF` from the **exact official AMD 26.11.1 installer**.

```powershell
dotnet run --project Source/tools/Finesse.SourceFirstVerifier/Finesse.SourceFirstVerifier.csproj -c Release -- `
  --display-root "C:\PATH\TO\Packages\Drivers\Display\WT6A_INF" `
  --source-manifest "C:\PATH\TO\verified-original-194.sha256" `
  --lenovo-policy "C:\PATH\TO\authorized-go1-policy.txt" `
  --profile Go1 `
  --output-dir "C:\PATH\TO\NEW\unsigned-candidate-Go1"
```

Accepts `Go1`, `GoS`, or `Go2`. The output path must not exist. The tool checks exact source and candidate hashes before publishing the isolated output directory. Its output includes `PreparedDriver/` (194 unsigned package files), `OriginalAMD/u0206030.cat` (the unchanged catalog for **later** verification), and `M195-UnsignedSourceHandoff.json` (evidence recording **no signing or installation**).

**Do not install the package output directly.** The original AMD catalog's signature and coverage have not been verified by this CLI. Microsoft-signed catalog verification and the local Finesse catalog must still be performed in the real Windows lifecycle.

## Why the old GUI cannot simply call this yet

Frozen M194 `M182InstallLifecycleWorkflow.RunAsync` builds an embedded `M80Recipe.LoadForM194(profile)` before AMD download/extraction; `M80Pipeline.PrepareAsync` copies original source based on that already-materialized private recipe.

M195 needs this exact reordering:

1. Detect exact supported model; retain original-driver backup and necessary recovery proof
2. Verify original AMD installer signer, pinned size/SHA-256, and safe download location using the source-only contract
3. Extract the authenticated original AMD INF/DAT/kernel and original main catalog
4. Verify approved 194-file source manifest and external Lenovo policy rows
5. Build exact INF/DAT in memory and run the new source-first 194-file staging handoff
6. **Regenerate and sign** the new main catalog; verify original AMD catalog and 14/14 dual coverage, scoped cleanup, and staging/binding
7. Install and verify AMD Software **before** one planned reboot, with first postboot observation prior to any recovery

**Not complete:** The old GUI/pipeline refactor, publishable source-selection manifest, legally redistributable registry-policy source, official signature-verification wiring, catalog generation, installer/rollback integration, and physical M195 testing. Do not claim the public end-user installer has passed CI yet.

Frozen M194 is preserved. The existing Code 0 Go 1 / Go 2 hardware evidence belongs to M193/M194, **not to M195**.
