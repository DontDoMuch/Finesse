# M195 Step 8 — internal native lifecycle integration (draft, NOT an installer)

**Status:** Windows .NET 10 compilation and synthetic regression tested. **No M195 physical bind, Secure Boot Code 0, rollback, catalog registration, postboot, AMD Software or single-EXE validation has occurred.** No end-user invocation path is enabled.

## Grounded parent and exact scope

M194 baseline archive SHA-256: `FE85DFB799266FAB5F4C47E658A24232D828EF31EE7FC6EFA70EEB60F9592B9C`.

Frozen M194 source SHA-256: `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`.

References: frozen `M179StageBindOperation.cs`, `M180RollbackVerification.cs`, `M182InstallLifecycleWorkflow.cs`, `M191-Validation.ps1`, `M115-V5-Catalog-Interop.ps1`. M193/M194 physical success belongs to those frozen builds, **not** M195.

This work adds:

- `M195Step8Sequence.cs`: pure, testable **sign -> stage -> bind -> audit** coordinator. Validates assigned target version/INF, permitted preboot Code 0/43, required retained signer and signed catalog identity. On later fatal integrity errors, invokes recovery. Incomplete/absent optional M191 catalog coverage is recorded as incomplete, not an invented unconditional blocker.
- `M195WindowsStep8Native.cs`: internal Windows implementation against the **existing** signer, staged-published-DriverStore proof, native NewDev binder, 14x4 audit, and narrowly scoped original-catalog registration. Consumes the real, unmodified Inf2Cat `Result`, including exit code, rather than a forged GUI DTO.
- `M195WindowsNativeBind.RestoreAfterStep8FailureAsync`: revalidates the original driver export manifest, exported files, and exact INF before requesting rollback; M180 reports restored only upon active original INF/version/instance, **Code 0 and no reboot pending**.
- `M195OriginalAmdCatalogRegistration.Result.FinalAssessment`: preserves the **measured post-registration** original/local catalog coverage counters.
- `M195LifecycleSequence`: retains M194's original optional M191/extension audit policy; does not conflate failing to collect optional evidence with a broken driver installation.
- Expanded vendor-independent offline tests for Go1 Z1 Extreme, Go S Z1 Extreme, Go2 Z2 Extreme, and fault injection at signing, staging, assigned version, and postbind catalog integrity.

## Actual Windows transaction when explicitly integrated later

1. Real source/Inf2Cat receipts + manifest + exact per-device candidate validated by `M195LiveSigningPreflight.Validate`.
2. One retained LocalMachine signer; catalog verified; no additional UAC prompt or temporary CurrentUser Root cycle.
3. `SetupCopyOEMInfW` stages the signed driver. Before binding, resolve the published `oemNN.inf` to the actual DriverStore package, hash the mapped INF and signed CAT, and compare to the verified candidate.
4. `UpdateDriverForPlugAndPlayDevicesW` gets an explicit `out bool rebootRequired`. **Step 8 never reboots.** Native binding and assigned active INF/driver version must match the exact device. Preboot Code 43 is only provisional.
5. Run read-only M191 four-way 14-file audit: authentic AMD CAT file coverage, new local CAT file coverage, original catalog registration, local catalog registration, plus published DriverStore INF/CAT and signature-policy checks. Register **only** a pinned authentic original AMD catalog when missing and independently proven; re-enumerate original and local registration.
6. Fully verified four-way 14/14 may be reported as 14; partial or unavailable verification is recorded as **not fully verified**. Missing optional measurements do not automatically force recovery. Actual tampered/missing DriverStore files, changed active GPU assignment, altered CAT identity and similar hard integrity faults force a recovery attempt.
7. On critical failure *after* binding, recheck the original per-file SHA manifest and request the original INF. Verify actual recovery state using M180; pending reboot, provisional Code 43, missing observation, mismatched original INF/version or a NewDev API return alone do not establish a recovered baseline.
8. Write a Step8 JSON receipt referencing signer/staging/bind/audit/recovery evidence, including native failure evidence even if a call threw before returning a successful object.

## Explicit restrictions and feature parity

The Step8 native method is `internal`; the current `M195WindowsPreparationActions.SignOnceStageBindAndVerifyDualTrustAsync` **still refuses to execute** because the end-to-end AMD Settings/reboot/postboot workflow is not yet safely available. No GUI, downloadable installer, or release is wired to this path.

Protected invariants remain: exact three device profiles, 26.11.1 original-file provenance, positional 3-byte DAT/MetroHash64 reconstruction, Phoenix/Strix-specific Lenovo INF/CopyINF source closure, original AMD binaries unchanged, one retained signer, scoped cleanup, official catalog registration, AMD Software before reboot, first postboot observation before recovery, and no auto-install of VC++.

**Still incomplete:** real physical signing/staging/binding/catalog-registration test, MSI exact identity/1603 WiX fallback Step 10, a single durable postboot registration/restart Step 11, first-observed postboot verification and backup recovery Step 12, production GUI wiring, real-device tests for all three supported profiles, and redistributable source/public single EXE parity. Frozen M194 and `main` remain unchanged.

### Engineering commands (nonmutating)

```powershell
git clone https://github.com/DontDoMuch/Finesse.git
cd Finesse
git switch m195-public-build-ci-foundation
dotnet run --project Source/test/Finesse.Compatibility.OfflineTests/Finesse.Compatibility.OfflineTests.csproj --configuration Release
```

This runs synthetic / read-only tests only, not a GPU driver installation. The `dotnet` SDK is needed by **developers**, not intended end users. Verify the latest CI run and full installer source gate independently; an offline pass is not release acceptance.
