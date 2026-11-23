# M195 — self-contained one-button Finesse.exe host: build milestone

**Status: WINDOWS SELF-CONTAINED EXE BUILT — NOT LIVE HARDWARE VALIDATED, NOT SIGNED, NOT RELEASED.**

This is development on draft PR #2 only. Frozen M194, public `main`, and existing releases must remain unchanged. A hosted Windows build does not authorize publication or physical acceptance.

## Source provenance

The exact M194 frozen baseline used for the implementation:

- Outer `M194-FROZEN-TESTED-BASELINE-20261122.zip` SHA-256 `FE85DFB799266FAB5F4C47E658A24232D828EF31EE7FC6EFA70EEB60F9592B9C`
- Full 26.11.1 source ZIP SHA-256 `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`

Inspected actual frozen `Finesse.Gui/Program.cs`, `M182InstallLifecycleForm.cs`, `M138ToolBootstrap.cs`, `M140AdrenalinIntegration.cs`, and the three exact M194 profile policy sections. The source files reconstructed into M195 contain only code, SHA-256 contract metadata and small functional HKR configuration directives. They do **not** contain or redistribute any AMD/Lenovo original driver EXE, INF, DAT, CAT, SYS, DLL, proprietary DriverStore package, certificate or private signing key.

## New host and Windows CI

- `Source/src/Finesse.WindowsHost/Finesse.WindowsHost.csproj`: Windows Forms `net10.0-windows`, `AssemblyName=Finesse`, `win-x64`, references the internally source-verified compatibility library and embeds only the authentic 194-line SHA manifest as **metadata**.
- `app.manifest`: `requireAdministrator`, `uiAccess=false`. One Windows UAC elevation at startup rather than separate PowerShell launches or later UAC re-prompts. This is a process manifest check, **not** a tested user-session UAC claim.
- `Program.cs`: accepts **no args** for regular GUI or exactly `--m195-postboot` for the scheduled-task resume. Requires the currently elevated administrator process. Postboot dispatch happens before any normal installer form; never install again on the resume argument. Composes the signed source-first lifecycle on the exact auto-detected hardware profile.
- `M195InstallForm.cs`: one enabled action — **Install AMD Driver**. Dark-purple clean GUI, twelve user-readable progress stages, no Mxxx/private logs in the UI. Before any native driver operation it verifies the exact GPU, pinned policy and trusted portable tool source. Step 10 is finished before the single planned restart; the progress remains at 91% pre-reboot instead of falsely displaying postboot Code0.
- `M195PostbootStatusForm.cs`: visible Windows login verification result, distinguishing exact target Code0 and installed AMD Software from the recovered original baseline or unverified recovery. It does **not** present a recovered original driver as successful new-driver installation.
- `M195FrozenProfileRegistryPolicies.cs`: the exact functional 26/30/28-row Lenovo HKR policy sets are taken from user-provided M194 Go1 (M79 recipe), Go S/Go2 (M147 source) and verified by SHA256:
  - Go1: `0C8F89B4BE1CAB61123B053DA24466635D559850B65C694BA225BFD36C22A7C7`
  - Go S: `E3AB1232BD66F0DD1FA4E862307ABC544295F8BA7F37B4C6E936A2585996BD56`
  - Go2: `420993CA99FD79B08C9440665F50831E757D97274C490D29C9A5C902ADBF728E`
  These are input configuration rows, **not** a redistributed AMD driver. Runtime additionally reconstructs and checks the frozen SHA256 of the complete profile INF using authenticated original AMD 26.11.1 bytes; a wrong or changed row is rejected.
- `M195PinnedToolBootstrap.cs`: automatic user-transparent prerequisite acquisition copied from frozen M138/M140 provenance. SHA-pinned official 7zr 26.04, 7-Zip 26.06, Microsoft SDK BuildTools 10.0.28000.2526 and WDK.x64 10.0.28000.2526 NuGet packages. SHA/size + HTTPS allowlist + no auto redirects to arbitrary hosts + exact extracted Inf2Cat/SignTool hashes. These executables are cached under `%LocalAppData%\Finesse\portable-cache`. **No SDK/WDK or 7-Zip installer is installed** on the user's machine. AMD installer source is still independently authenticated through the existing official AMD 26.11.1 downloader.
- `.github/workflows/m195-windows-host-build.yml`: separate `dotnet publish` to Windows runner temp for `win-x64`, `--self-contained true`, `PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`; verifies one EXE (aside from optional PDB), SHA-256 and unsigned signing status. **It intentionally does not upload or release the EXE**.
- Existing `windows-ci.yml` also now requires the WinForms host self-contained publish to succeed as part of the offline compatibility checks, rather than allowing a green compatibility library while the actual GUI does not build.

## Physical lifecycle

The GUI runs `M195LifecycleSequence.RunPrebootAsync` on a worker thread with the internal `M195WindowsFullLifecycleActions`:

1. Read-only exact physical Go1/GoS/Go2 GPU identification
2. Identify currently assigned Windows GPU driver/INF
3. SHA-verified original-driver recovery export; optional M192 cleanup *only after* backup
4. Source-authenticated official AMD 26.11.1 acquisition
5. Authenticate all 194 original AMD source files and intact original AMD catalog
6. Rebuild the exact profile INF, CopyINF changes, and 3-byte positional DAT/MetroHash64 from original AMD sources, without modifying original kernel binaries
7. Run real pinned WDK Inf2Cat and keep its actual process-result proof
8. One retained local signer, exact staged driver and native bind, dual catalog trust audit / registration with genuine receipts
9. Observe preboot GPU and preserve provisional Code43 without declaring new-driver success
10. Verify AMD Settings Store status, authentic pinned MSI, first silent `/qn /norestart` install, and only on a verified true WiX downgrade blocker do precisely scoped uninstall/retry; verify MSI installation and frontend
11. Register durable selfsame SHA-256 Finesse.exe under admin-owned Program Files and one-shot elevated login task, then request exactly one normal Windows restart
12. On postboot login, capture immutable first physical GPU state **before recovery**. Only exact driver/INF/version, device Code0 and AMD Software is target success; otherwise reverify recovery export and observe original Code0. Export combined preboot+postboot diagnostic ZIP + SHA-256 under `Downloads\Finesse\Reports`.

The user-facing GUI does not display legacy Mxxx labels or detailed logs; those are retained in engineering receipts. No automatic VC++ redistributable installation, no testsigning, no Secure Boot disable, and no antivirus/EAC compatibility gate.

## Verified CI acceptance (source-build only)

The Windows hosted runner compiled and published a **single `Finesse.exe`** with size 116,786,446 bytes. The published Windows image's Authenticode status was **NotSigned**. It does not require a separately installed .NET runtime. The CI did not execute the WinForms UI, native GPU binding, local signing, Microsoft Store cleanup, AMD MSI, scheduled task, reboot or recovery.

The current Windows offline suite independently passed **372** synthetic/source-contract regression checks plus four vendor-free developer utility builds with 0 warnings, 0 errors. The source audit reported **66** source files, **194** original AMD hashes and **0** vendor driver binaries/private keys in the public repository.

## Release blockers — must not relabel as completed

1. **Actual Windows 11 runtime walkthrough and code-signing:** The generated EXE is **unsigned** and can be blocked by Smart App Control / reputation or other Windows code-integrity policy. Merely locally signing the installer does not establish consumer code-signing reputation. Do not claim a Store- or Microsoft-trusted public release.
2. **End-to-end physical driver test:** Source compile/Win32 read-only CI is not proof of source extraction, Inf2Cat, signer trust, target GPU Code0, WiX/MSI fallback, postboot scheduled-task behavior or verified rollback on actual Go1/GoS/Go2 hardware with Secure Boot ON and testsigning OFF.
3. **Tool provenance at runtime:** SHA pins and URLs were copied from M138/M140 frozen. Runtime downloads, redirect patterns, all required Inf2Cat/SignTool sidecar DLL dependencies and executable startup on a *clean* Windows environment still need a live source/test cycle. If current vendor URLs or executable dependencies change, the app should fail on exact verification rather than silently fetch unpinned substitutes.
4. **Unified diagnostics runtime acceptance:** The durable postboot ticket now preserves the admin-owned preboot workspace. The ZIP exporter combines scoped source, Inf2Cat, signing, staging, MSI, backup and recovery diagnostic JSON/log/text with immutable postboot first-GPU snapshots and outcomes, plus per-file and whole-ZIP SHA-256. No AMD/Lenovo driver binaries, INF/DAT/CAT/SYS/DLL/EXE, private keys or unrelated files enter the ZIP. This is source-implemented and synthetic-tested; a real clean-Windows installation is still needed to establish complete on-device evidence closure.
5. **Actual host UX acceptance:** test one UAC prompt, clean progress, no unexpected visible MSI dialogs, one planned restart only after AMD Software, login postboot form, and no repeating driver recovery on subsequent login.
6. **Release and governance:** keep PR #2 draft, `main` and frozen M194 untouched; do not upload/sign/release an executable until physical proof and complete engineering checks are documented. No automated Windows restart or live install is performed by CI.

**Conclusion:** `M195_SINGLE_EXE_BUILD_ONLY_PASS`; `M195_LIVE_INSTALL_ACCEPTANCE_UNVERIFIED`.
