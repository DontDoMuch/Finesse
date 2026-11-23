# M195 native GPU binding and M180 recovery port (internal draft)

**Scope:** Frozen M194 `M179StageBindOperation.cs` / `M180RollbackVerification.cs` -> public-source `Source/src/Finesse.Compatibility/M195WindowsNativeBind.cs`.

**Do not run as an installer or claim a successful physical AMD install.** The binder remains internal to the compatibility assembly and is NOT wired to `M195WindowsPreparationActions.SignOnceStageBindAndVerifyDualTrustAsync`, the GUI, or a release. A Windows CI compile/test pass cannot exercise real NewDev binding or rollback.

## Frozen source reference

- `M194-FROZEN-TESTED-BASELINE-20261122.zip` SHA-256: `FE85DFB799266FAB5F4C47E658A24232D828EF31EE7FC6EFA70EEB60F9592B9C`.
- Nested/standalone `M194-26111-THREE-DEVICE-FULL-LIFECYCLE-SOURCE.zip` SHA-256: `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`.
- Actual source consulted: `Source/src/Finesse.Gui/PrivateM80/M179StageBindOperation.cs`, `M180RollbackVerification.cs`, `M182InstallLifecycleWorkflow.cs`.
- The frozen M193/M194 Go1/Go2 physical Code 0 results do **not** constitute an M195 hardware result.

## Implemented

1. `RequirePlan` is a pure, nonmutating identity gate. It compares the actual present Display devnode, exact profile and instance, active original INF/version, staged `oemNN.inf`, candidate INF install-section mapping, and exact observed hardware ID. It refuses cross-device, stale baseline, renamed INF, and ambiguous mapping.
2. `BindAsync` is internal and requires an elevated Windows process and a prior actual `M195WindowsDriverStoreStage.StageReceipt`. It revalidates the full original recovery export if one exists; Go S Z1 Extreme and Go 2 Z2 Extreme require a verified export, while Go1 retains frozen optional-backup behavior. It verifies the staged **published INF, DriverStore-mapped INF and signed CAT** against the candidate before attempting native mutation.
3. It calls `UpdateDriverForPlugAndPlayDevicesW` with `INSTALLFLAG_FORCE=1` and an actual `out bool rebootRequired`; it does not call shutdown or schedule any reboot. It captures Win32 errors, repeatedly observes the actual GPU instance/INF/version/code, and accepts only exact target Code 0 or provisional Code 43 as **preboot assignment**, never as postboot success.
4. Failure or mismatched assignment triggers attempted recovery using the verified original INF. The recovery evaluation ports frozen M180's exact semantics: API success alone is not recovery; wrong instance, INF, version, Code 43, no observation, and **any pending required reboot** stay unverified. Observed exceptions and recovery exceptions are recorded as evidence requiring review.
5. An evidence JSON receipt records native bind outcome, assigned device state, deferred restart and attempted recovery. Initial `READY_TO_BIND` evidence is written before mutation. No candidate or original driver package is deleted; no blanket certificate cleanup is performed.

The exported baseline's `SHA256SUMS.txt` and each exported file are checked again before binding; original exported INF is matched by hash against the active Windows `oemNN.inf`, not inferred from the backup's filename.

## Verified by automation, NOT hardware

- Exact Go1 Z1 Extreme, Go S Z1 Extreme and Go2 Z2 Extreme plans are tested with synthetic devnode data; wrong hardware, instance, active INF/version, staged name and install section are rejected.
- Synthetic M180 recovery scenarios cover actual original Code 0, pending restart, API rejection, absent observations, Code 43, changed version and changed GPU instance.
- CI on hosted Windows compiles .NET 10 and runs the offline test executable plus four developer-tool builds. CI deliberately never invokes `BindAsync`.
- No proprietary AMD source binaries or test certificates are introduced to the public repository.

## Must be completed before any end-user release

- Complete the live Step 8 orchestrator: authenticate and sign once; stage; bind using this internal method; invoke scoped original AMD CAT registration and the real **four separate 14/14** trust checks against the assigned DriverStore; report incomplete optional checks distinctly rather than inventing an unrequested hard stop.
- Integrate the frozen 26.11.1 Step 10 AMD Settings MSI discovery, exact product/upgrade identity and narrowly validated 1603/`WIX_DOWNGRADE_DETECTED` fallback. AMD Software must install **before any restart**. No automatic VC++ redistributable install or prompt.
- Add one planned restart with durable postboot resume; capture the **first actual postboot GPU observation before any rollback**; accept target only with exact INF/version, Code 0 and AMD Software detected; otherwise restore and independently verify the original driver.
- Retain all three exact profiles, release-specific DAT positional records/MetroHash64, INF/CopyINF closure, signed original binary provenance and cleanup guards. Preserve M194, `main`, releases, and user-facing installer until full parity and actual Windows hardware tests establish success.

**Current status:** `BUILD_ONLY` / `M195_INTERNAL_NATIVE_BIND_NOT_LIVE_VALIDATED`. No M195 physical bind, Code 0, rollback, MSI, postboot, or end-user EXE is claimed.
