# M195 — Internal full lifecycle integration (source-only, not a public installer)

**State:** SOURCE INTEGRATED / OFFLINE WINDOWS CI. **No M195 physical install or hardware acceptance.** The internal adapter is *not* called by the public GUI or an installer entry point; no released EXE will execute these new paths automatically.

## Frozen evidence and nonnegotiable behavior

The benchmark remains the frozen M194 26.11.1 full lifecycle source ZIP SHA-256 `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1` within the M194 outer frozen archive SHA-256 `FE85DFB799266FAB5F4C47E658A24232D828EF31EE7FC6EFA70EEB60F9592B9C`.

The three exact profile targets remain Go1 Z1 Extreme `PCI\VEN_1002&DEV_15BF&SUBSYS_381217AA&REV_04`, Go S Z1 Extreme `PCI\VEN_1002&DEV_15BF&SUBSYS_380C17AA&REV_04`, and Go2 Z2 Extreme `PCI\VEN_1002&DEV_150E&SUBSYS_381C17AA&REV_C5`. The original 194-file AMD source manifest, per-profile INF and Lenovo CopyINF, positional 3-byte DAT record and release-specific MetroHash64 reconstruction, preserved AMD original binaries, original/locally signed 14-file dual-catalog evidence, and stage/bind/postboot identity verification remain source contract requirements.

**Security and UX invariants:** Secure Boot stays enabled; no Test Mode. One elevated Finesse process / one UAC prompt at most; one planned restart only **after AMD Software installation**. Preboot Code43 is provisional, never postboot success. First postboot GPU snapshot is persisted before recovery. Recovery must prove the original driver, exact instance, INF/version, Code0 and no pending reboot. No VC++ Redistributable auto-install, no EAC or overlay installation blocker, no legacy milestone spam in GUI. Frozen M194 and public `main` unchanged.

## Implemented in this continuation

### 1. `M195WindowsFullLifecycleActions.cs` — internal 12-step native orchestration

This `internal sealed` adapter implements the existing M194-derived `M195LifecycleSequence.IWindowsInstallationActions`. It composes the tested preparation-only adapter but **does not enable** the latter's public NotSupported install methods. It stores and requires identity of the actual in-process receipts:

- Steps 1–3: exact GPU/profile, existing assigned INF/version, eligible verified baseline export (Go S/Go2 mandatory; Go1 original policy preserved)
- Steps 4–6: the exact authenticated official AMD 26.11.1 installer and original 194-file extraction, then the correct profile's INF and DAT candidate
- Step 7: invokes the **real** `Amd26111UnsignedCatalogGenerator.GenerateAsync` and retains the full process-result object (`ExitCode`, SHA-256, logs). Crucially it **does not reconstruct or fabricate** a positive Inf2Cat process receipt from a smaller GUI DTO
- Step 8: passes that exact receipt and prepared candidate into `M195WindowsStep8Native.ExecuteAsync` for one retained signer, SetupAPI staged driver, native NewDev GPU binding, original AMD catalog registration and fully measured optional 14x4 dual trust
- Step 9: delegates to the independently implemented exact preboot GPU observation. Preboot Code43 cannot prove postboot health
- Step 10: consumes the **same original authenticated AMD source/extraction** and the actual bound GPU receipt in `M195WindowsStep10Native.ExecuteAsync` for pinned MSI-first software installation and exact WiX downgrade fallback, with `/qn /norestart`
- Step 11: requires the actual Step10 result to be installed and independently verified *before* boot identity, task registration or restart request. The pending ticket must match the in-process physical GPU, signed CAT, official AMD installer, original INF/version and verified backup. Existing Step11 code persists one transaction and requests one Windows restart
- Any Step9–11 failure **before a restart is accepted** invokes `M195WindowsNativeBind.RestoreAfterPrebootFailureAsync` against a re-hashed original-driver export. Failed diagnostic-folder writes cannot themselves suppress rollback. Code0 must actually be observed; a recovery attempt is not automatically success

The wrapper has no public install method or real-EXE/GUI caller at this stage. It is **not** a standalone installer.

### 2. `M195PostbootDispatch.cs` — source-only command hook

The exact argument `--m195-postboot` is recognized; every other argument, including an extra second argument, returns *not handled* without any Windows GPU/MSI mutation. The intended future executable bootstrap must **explicitly call** `TryHandleAsync(args)` before constructing the normal GUI. When called from a valid staged Finesse process, it invokes existing `M195WindowsPostbootActions.ResumeAsync` and exits with distinct statuses for exact target Code0, recovered baseline, and recovery-unverified. The code requires that the process be the actual **Finesse.exe** host. `dotnet.exe` or another developer host cannot be silently copied and registered as the one-shot executable.

**The actual source of the future GUI executable does not yet invoke this hook.** Adding a helper class is not equivalent to building or publishing a working postboot entrypoint.

### 3. `M195PostbootEvidenceExporter.cs` — real ZIP + SHA-256 sidecar

On postboot completion, scoped M195 first snapshot, outcome and optional recovery receipts are collected into a read-only archive under `%USERPROFILE%\Downloads\Finesse\Reports\Finesse-M195-Postboot-<transaction>.zip`. The corresponding `.zip.sha256` and archive-internal JSON inventory record the output SHA-256 and per-member SHA-256. Source evidence is not modified; unrelated user files are excluded, names cannot escape the archive, duplicate output is rejected, and file reparse points are refused. A download/ZIP error records a warning instead of changing the physical GPU's Code0 outcome or triggering another reboot.

**Scope:** this newly created archive collects **postboot** files only. Combining preboot driver-signing, MSI, rollback, Task Scheduler and postboot receipts into one user-facing evidence ZIP still needs to be implemented before release.

Windows CI exposed and then verified the fix for a file-rename error: close the SHA-256 read handle before `File.Move` of the completed ZIP.

### 4. `M195WindowsNativeBind.RestoreAfterPrebootFailureAsync`

This new native path rechecks full original export SHA hashes before any rollback, requires the exact observed current GPU and supported HardwareID, rejects foreign assigned drivers, and returns a verified original Code0 receipt only after Windows confirms matching original INF/version and no required reboot. It does not request a second restart.

## Regression and test boundaries

The new hosted Windows offline checks test: strict postboot dispatch, wrong-host rejection, internal-only lifecycle accessibility, rejected restart before actual MSI completion, rejection of synthetic Step7 sign/bind receipts, actual ZIP archive creation and reading, omission of unrelated files, output SHA-256 verification, overwrite refusal, and invalid transaction identity. These run on a synthetic temp directory on hosted Windows.

The previous 336 offline tests also continue to cover three exact device profiles, frozen original source recipe and independent 14x4 trust, native source and MSI metadata APIs, Step8 failure recovery, Step10 MSI downgrade policy, and Step11/12 boot-boundary and rollback-negative cases.

**No hosted CI step installs the AMD driver, manipulates Windows certificates, uninstalls MSI or Store packages, creates scheduled tasks or reboots.** Windows CI success means these *source and synthetic* claims only. The full installer source gate is intentionally skipped.

## Hard remaining work before a public release

1. **Real single-file Finesse.exe:** self-contained Windows GUI with one Install AMD Driver button, exactly one requested UAC elevation, clean 12-step progress, integrated internal lifecycle adapter, and actual `--m195-postboot` startup dispatch. No SDK or PowerShell command requested of end users.
2. **Automatic verified tool acquisition:** official Windows developer tools / Inf2Cat / SignTool, pinned 7zr and portable 7-Zip sources must be obtained and verified by the application, not manually supplied by end users. Preserve frozen M194 tool provenance.
3. **Preboot/postboot evidence closure:** combine all source/sign/bind/dual-14/MSI/task/reboot/first-boot/recovery receipts into user-facing evidence ZIPs under Downloads without losing the immutable Program Files originals.
4. **Real source and device verification:** re-run actual frozen M194 source hashes; exercise the complete M195 self-contained build on a clean Secure Boot ON, Test Signing OFF Windows 11 instance, with verified signing, authored MSI installation **before** the single reboot, then first postboot Code0 and AMD Software. Repeat supported physical profiles where available; preserve scoped rollback.
5. **Acceptance and packaging:** do not merge PR #2 or ship/relabel as an M195 release until the full source gate, installer runtime, genuine first-postboot recovery, zero silently missing features, and user-facing GUI parity pass. Existing M194 hardware evidence cannot be reused as evidence for M195.

The draft branch intentionally retains `M195WindowsPreparationActions` as preparation-only: it still explicitly rejects direct Step8 and Step10 installation. Only the new **internal** full adapter links the components. This separation prevents a partially wired public installer from silently running native Windows mutation code.
