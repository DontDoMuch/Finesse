# M195: Windows live signing, Driver Store staging, and dual-catalog groundwork

**Engineering / draft source only — NOT an end-user installer.** This document is grounded in the frozen M194 archive `M194-26111-THREE-DEVICE-FULL-LIFECYCLE-SOURCE.zip`, SHA-256 `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`. Relevant original sources:

- `Source/src/Finesse.Gui/PrivateM80/M175SigningOperation.cs` — disposable offline signer **not reusable** for installation
- `Source/src/Finesse.Gui/PrivateM80/M179StageBindOperation.cs` — retained live signer, SetupCopyOEMInfW, UpdateDriverForPlugAndPlayDevicesW, catalog registration
- `Source/src/Finesse.Gui/PrivateM80/M179SignerCleanup.cs` — remove only a precisely owned signer after **failed** staging
- `Source/src/Finesse.Gui/PrivateM80/M191Validation.cs` and `Assets/M191-Validation.ps1` — real driver-store mapping, original/local catalog membership, 14/14 registered trust
- `Assets/M115-V5-Catalog-Interop.ps1`, `Assets/M115-PINNED-CONTRACT.json` — corrected native CryptCATAdmin v2c API and 14 original critical-file SHA identities

## Added to M195 public compatibility source

| File | What it implements | Stage |
|---|---|---|
| `M195LiveSigningPreflight.cs` | Read-only proof of the full **194-file** candidate, authentic unchanged original AMD catalog, pinned unsigned CAT, exact pinned Microsoft SignTool SHA-256 and per-profile manifest | Pre-sign read-only |
| `M195LiveCatalogSigner.cs` | Concrete single LocalMachine non-exportable signing identity, retained public Root + TrustedPublisher trust, one SignTool `sign`, `verify /pa` and INF `verify /pa /c`, retained-cert proof and file hash recheck | **Trust-changing**, intentionally internal and **not connected to GUI** |
| `M195WindowsSignerCleanup.cs` | Scoped certificate cleanup for newly generated Finesse signer, only after the stage API reports failure | **Trust-changing**, not blanket cleanup |
| `M195WindowsDriverStoreStage.cs` | Concrete M179 native `SetupCopyOEMInfW` signed package **stage only**, Verify Win32 result + Windows published `oem#.inf` SHA, retain signer if Windows staged | **Driver Store-changing**, intentionally internal and **not connected to GUI**; no bind |
| `M195AmdDualTrustTargets.cs` | Original 14 critical AMD 26.11.1 file names and SHA-256 values, checked against the public 194-file manifest | Metadata only |
| `M195WindowsCatalogRegistry.cs` | Read-only corrected M115 `CryptCATAdminAcquireContext2` + `CalcHashFromFileHandle2` + catalog enumeration of exact registered file hashes | Read-only registry lookup only; **does not register catalogs** |

No full AMD INF/DAT, AMD driver binaries, catalogs, signing private keys or private M194 recipe ZIPs are included.

## Ordering and recovery details

The M194 distinction between offline and live signing is preserved: the offline signer creates **temporary current-user** trust and removes it, while the installation signer requires **retained LocalMachine trust** for the installed/staged package. The public signing source is not linked to Step 8 of the production GUI, because signing and staging must become one audited transaction with the still-missing native binding and recovery operation.

The signing preflight verifies **all 194 package members**, not just the INF/DAT. The signer checks that those hashes have not changed immediately before and after the signing operation. A stage API failure attempts cleanup **only** of the new uniquely named Finesse signer, with status recorded. If `SetupCopyOEMInfW` reports staging success, the signer is retained even if a subsequent receipt check fails, since the staged driver may depend on it.

The 14 critical file hashes reproduce the M191/M115 original AMD kernel/graphics coverage reference, but **hashes alone do not prove catalog trust**. A complete verification must use the real staged Windows Driver Store paths, exact /pa /kp catalog signatures, catalog membership for both exact signed catalogs on all 14 files, and re-enumeration from CryptCATAdmin after any original catalog registration.

## Not yet implemented or verified

- No live signing, certificate-store changes, real driver-store staging, original AMD catalog registration or real binding has been **executed** by M195.
- The public source still lacks the complete M191 **14/14 dual-catalog trust validation** and source-proven original AMD catalog registration transaction. `M195WindowsCatalogRegistry` is read-only lookup, not the validator.
- The M179 `UpdateDriverForPlugAndPlayDevicesW` GPU bind and hash-verified recovery on failure are not yet linked to the M195 production adapter.
- The M140 AMD Software MSI operation (including version-scoped 1603/WIX workaround), postboot registration, one planned reboot, first postboot observation and rollback are still pending.
- The entire redistributable Finesse GUI and full source-build gate are not yet available. M195 is **not** physical Go 1/Go 2 Code 0 evidence and is not a released executable.
- A local self-signed catalog, successful `/pa` output, or registered catalog membership alone is **not** proof of Microsoft kernel-signing acceptance with Secure Boot enabled; physical postboot testing remains required.

## Validation boundary

The Windows CI suite builds the .NET 10 public source using the **system-wide installed SDK** and tests negative signing inputs without creating certificates. It also includes a **parse-only** Windows PowerShell 5.1 validation of the embedded retained signer script; no script commands are executed by the parser. The native catalog-enumeration test is read-only against a Windows runner's system file. All physical install claims remain reserved for actual device evidence.

Keep this PR draft and frozen M194 unchanged. Continue by porting full 14/14 catalog coverage and registration with explicit evidence, then the controlled bind/rollback and full MSI/postboot lifecycle.
