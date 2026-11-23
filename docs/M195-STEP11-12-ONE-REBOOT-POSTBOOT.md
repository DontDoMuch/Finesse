# M195 Steps 11–12 — durable one-restart / postboot recovery engineering record

**Status: Windows CI/offline source integration only, NOT a public or physically executed installer.** Do not claim fresh M195 driver binding, MSI, scheduled task, actual reboot, physical Code 0, or rollback based on these synthetic/hosted tests.

## Frozen reference sources verified

- Outer `M194-FROZEN-TESTED-BASELINE-20261122.zip` SHA-256: `FE85DFB799266FAB5F4C47E658A24232D828EF31EE7FC6EFA70EEB60F9592B9C`
- Nested `M194-26111-THREE-DEVICE-FULL-LIFECYCLE-SOURCE.zip` SHA-256: `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`

Audited frozen `M182InstallLifecycleWorkflow.cs`, `M182Postboot.cs`, `M182Lifecycle.cs`, `M154PostbootTaskRegistration.cs`, `M155PostbootTaskRegistration.cs`, `M141PostbootResume.cs` and M180 baseline verification. M194 experimental hardware results belong to the **frozen M194** lineage, not to this M195 draft.

## New source and ownership

- `M195PostbootTicketContract.cs`: pure exact transaction ID / expected driver+AMD installer SHA / OEM INF / profile / baseline-version / boot identity contract. Go S/Go2 must retain verified backup location; Go1 preserves the frozen optional baseline behavior. A new boot is determined using Windows UTC boot-time estimates, with frozen M182's >30-second threshold. Same-boot logon/launch cannot trigger recovery.
- `M195LifecycleSequence.cs`: constructs the **complete immutable PendingPostboot** *before* registration (transaction ID, target and original INF, target and original version, exact GPU HWID and devnode instance, original verified AMD source SHA and signed CAT SHA, baseline backup directory, preboot Code43 flag and boot identity). The registered Task must report back the same transaction ID. Registration happens only **after** successful Step10 AMD Settings verification. One reboot request follows registration.
- `M195WindowsPostbootRegistration.cs`: stage a SHA-256-identical copy of the current Finesse EXE in `%ProgramFiles%/Finesse/Postboot/<SHA256>/<transaction>/Finesse.exe`, with JSON state beside it. No launch from user-writable Downloads. Require one elevated installer process. Create an interactive highest-run-level one-shot Task Scheduler entry, with the current elevated account SID matching the actual shell/desktop account, exactly one action and one logon trigger; verify task readback and promote staged ticket to `TaskReadbackVerified=true` only after task confirmation. On registration errors, attempt **scoped** removal of the exact owned task; never purge unrelated tasks.
- `M195WindowsPreparationActions.cs`: Step11-only source implementation now exposes real read-only Windows boot identity, task registration and `shutdown.exe /r /t 10` behind a fully verified registration record. The required fresh Step11 evidence folder is an optional configuration field; without it, registration refuses. **Steps 8, 10 and full preboot recovery are still disconnected here.**
- `M195WindowsPostbootRegistration.RequestSingleRestartAsync`: an atomic `CreateNew` reboot intent ledger prevents a second request under the same transaction even if the first Windows shutdown request returns an uncertain error. Shutdown is allowed **only after** successful registration, which is only available after Step10 in the full coordinator. Neither Step10 nor Step12 performs a reboot.
- `M195WindowsPostbootActions.cs`: internal-only postboot resume entry point, not yet wired to any GUI or command-line dispatcher. Requires staged process EXE path, SHA, trusted Program Files state, exact transaction and interactive SID. Capture the FIRST actual GPU PnP state and AMD Settings native-installed status, driver service, INF/version/problem code and actual boot identity to a **new immutable JSON file** before any recovery. Then and only then: confirm actual new boot; report target success only on exact Code0 driver+INF+version+device instance **and** AMD Software installation. If target fails after the real reboot, attempt recovery only for the exact selected physical GPU and matching export; require independently observed original INF, original version, Code0, and no outstanding reboot requirement (M180). Record recovery separately; never alter the first snapshot or reclassify recovered original as target success. Remove the one-shot task after completed postboot or recorded failure on a real new boot; leave it pending if the current session has *not* rebooted yet.
- `M195WindowsNativeBind.RestoreAfterPostbootFailureAsync`: consumes the persisted original INF/version and verified original package directory, independently re-hashes the entire manifest immediately before native rollback, binds with an actual physical HardwareID, observes the resulting exact baseline. No additional reboot request is allowed from Step12.

## Developer regressions

Offline synthetic regressions explicitly cover all supported profile identities, bad transaction GUID/HWID/source hash/target INF/driver version/backup metadata, same boot and clock jitter, different INF/driver version/physical instance, missing AMD Settings, postboot Code43, and rollback which did **not** actually become healthy. Real Windows CI additionally runs read-only SetupAPI enumerations; it does **not** schedule tasks, restart, mutate the driver or install software.

Regression rules:
1. Any Step11 registration failure must retain original error and any Task Scheduler readback/cleanup evidence. A new task does not become VERIFIED until the exact staged state and scheduled action/principal are re-read.
2. Any Step12 recovery must be preceded by an immutable `M195-FirstPostboot-<id>.json` receipt in admin-owned Program Files transaction directory.
3. If no genuine reboot or the physical GPU is not the exact target, **no rollback bind** is allowed.
4. Native recovery returning success but requiring reboot, observing wrong INF/version, or observing Code43 is **not** a verified recovery. No second auto-reboot.
5. A recovered original Code0 baseline is **not** target driver success.

## Critical open parity gaps

This branch is intentionally **not end-to-end executable**:
- `M195WindowsPreparationActions.SignOnceStageBindAndVerifyDualTrustAsync` still explicitly refuses to run the (separately compiled) internal native Step8 modules.
- `M195WindowsPreparationActions.InstallVerifiedAmdSoftwareAsync` still explicitly refuses to run the (separately compiled) internal native Step10 modules, pending complete integration.
- `M195WindowsPreparationActions.RestoreBaselineAfterPrebootFailureAsync` is still a deliberate unported operation.
- The installed public one-button GUI does **not yet dispatch** `--m195-postboot` to `M195WindowsPostbootActions.ResumeAsync`, and installer dependencies/portable 7-Zip acquisition and full 12-step parity have not been verified.
- No real Task Scheduler, shutdown, first-postboot or physical recovery experiment was performed by CI; simulated source and Windows API compile tests are **not proof of live behavior**.
- Postboot canonical evidence is currently stored under the staged admin-owned Program Files transaction (with cleanup receipts). Publication of a downloadable ZIP under `Downloads/Finesse/Reports` remains an integration requirement.
- Preserve hardware-specific AMD 26.11.1 exact INF + positional DAT/MetroHash64 transformations, original AMD graphics binaries unchanged, two catalog trust chains and all four 14/14 verification channels, Secure Boot ON, test signing OFF, and no unsolicited VC++ installer or EAC validation gate.

**Release decision: HOLD — draft PR only, not merged, no public executable.**

Source builds and offline CI cover controlled behavior, but the final acceptance criterion is a reproducible single-EXE lifecycle on fresh Windows for the three exact supported GPU profiles, authentic AMD Software **before** one planned restart, and first observed GPU Code0 **after** that restart.
