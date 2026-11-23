# M195 source-publication and CI/CD checklist

**Status:** draft requirements. Do not market a public GitHub-built driver installer until the checklist is demonstrably complete.

## Before publishing source

- [ ] Preserve frozen M194 source, launchers, and evidence as an immutable private checkpoint; branch forward
- [ ] Audit ownership/license of *every* embedded archive, INF, DAT, catalog and other third-party file
- [ ] Remove AMD-derived private assets from the public repository; acquire official AMD packages at runtime or through a documented BYO process
- [ ] Refactor `FINESSE_PRIVATE_LAB` so a normal Release build produces a functional, user-facing installer
- [ ] Audit installation behavior for secrets, hidden destructive operations, and unexplained trust changes
- [ ] Confirm the repository license is an intentional choice: PolyForm Noncommercial is source-available, not OSI open source

## System-installed .NET build and CI

- [ ] Normal system-wide .NET 10 SDK installation, discoverable on `PATH`; `global.json` selects installed version with deliberate roll-forward policy
- [ ] Developer build succeeds via `Get-Command dotnet` and `$PSScriptRoot`, no machine-specific or portable SDK paths; no implicit SDK downloads
- [ ] One `build.ps1` used locally and by GitHub Actions, with the .NET SDK installed on the Windows runner; pinned action revisions and least-privilege permissions
- [ ] All external process exit codes, compiler warnings, test results and output hashes verified
- [ ] Full test suite runs on clean Windows host, including per-profile and release-specific package transforms
- [ ] A full-source checkout produces a runnable EXE with no private-only compile symbol or manual file injection
- [ ] GitHub Release requires explicit approval, publishes checksum, source SHA, attestation, and build logs
- [ ] Document how to verify attestation; never imply an attestation proves Microsoft code-signing or hardware success

## Source-first 194-file handoff (current M195 status)

- [x] Read the actual frozen M194 `M182InstallLifecycleWorkflow`, `M80Recipe`, and `M80Pipeline` dependencies
- [x] Recover the original AMD 194-file inventory canonical SHA-256 and profile-specific candidate manifest fingerprints
- [x] Add vendor-binary-free 194-file original source manifest parser and strict path/hash/duplicate validation
- [x] Add an isolated unsigned stager that copies 192 unchanged files, replaces only the rebuilt INF/DAT, and preserves the authentic original main AMD catalog separately
- [x] Add Windows CI regression tests for synthetic 194-file staging, source tampering, output overwrite and unsafe relative paths
- [x] Add the source-first offline CLI compiled with normal system-installed .NET 10
- [ ] Publish and independently review the exact 194-file path/hash manifest in GitHub (metadata only; no AMD file payload)
- [ ] Publish or legally derive all 26/30/28 Lenovo per-profile registry directives without opaque embedded proprietary INFs
- [ ] Integrate source acquisition, WinVerifyTrust, authentic extraction and source-first stage into the **actual GUI**; do not claim the offline CLI is the GUI
- [ ] Run complete M194 lifecycle/regression tests in clean Windows CI and physical Secure Boot-on testing before release

## Compatibility and lifecycle (do not silently omit)

- [ ] Exact Go 1 Z1 Extreme, Go S Z1 Extreme, Go 2 Z2 Extreme hardware routing; reject unsupported variants
- [ ] Per-profile AMD Phoenix/Strix INF fidelity and original Lenovo policy preserved
- [ ] Verify original release-specific `CopyINF` references; perform only applicable proven removals
- [ ] Parse original `amdgcf.dat`: 12-byte header, 3-byte position-dependent records *only if the new release validates this schema*
- [ ] Decode, add required device/revision entries, re-encode offsets, recalculate release-specific MetroHash64, verify rebuilt DAT
- [ ] Preserve original AMD kernel and user binaries; verify SHA-256 against authenticated source
- [ ] Generate exact candidate catalog; silent local signing; register original AMD catalog where required
- [ ] Staged Driver Store dual-trust validation for **all 14 critical files**, plus appropriate signing checks
- [ ] Backup current working driver before mutation; scoped stale-package/cert cleanup preserving active recovery trust
- [ ] Bind selected candidate, record preboot state; do not claim provisional Code 43 is success
- [ ] Install/verify AMD Software including its precisely scoped MSI conflict fallback **before** one planned restart
- [ ] Capture first observed postboot INF, version, problem code, service *before* any rollback
- [ ] Recover known-good driver when required; record result and preserve evidence
- [ ] No automatic VC++ Redistributable installation; documentation only
- [ ] No Secure Boot disable, Test Signing toggle, speculative EAC blocking, or undocumented extra reboots

## Validation claims

- [ ] Label build-only success, catalog success, software install success, and physical GPU postboot success separately
- [ ] Preserve logs, hashes, signed-catalog evidence, exact driver version, first postboot state and recovery results
- [ ] Do not claim Go S physical success until real supported hardware achieves postboot Code 0
- [ ] Review a complete source-to-release diff before the first public release

This checklist complements, but does not replace, the full private `FINESSE_ENGINEERING_MASTER_HANDOFF_v1.md`.
