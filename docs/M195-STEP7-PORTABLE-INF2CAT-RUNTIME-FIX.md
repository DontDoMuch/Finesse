# M195 Step 7 — hardware-confirmed portable Inf2Cat dependency repair

**Observed physical test:** Lenovo Windows 11 test build, November 23 2026. Finesse displayed steps 1–6 complete and stopped at **Generate driver catalog**, before native signing/staging/binding (Step 8), AMD Software (Step 10), or restart. User shared the actual session receipt and stdout/stderr from:

`%ProgramData%\Finesse\Sessions\M195-01d1732c794a43c5813ed68fc108f4ef\`

Original exception: `System.IO.InvalidDataException: M195_INF2CAT_FAILED_EXIT_-1`. Actual `M195-Inf2Cat-stdout.txt`:

```text
Could not load file or assembly 'Microsoft.UniversalStore.HardwareWorkflow.SubmissionBuilder, Version=1.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35' or one of its dependencies. The system cannot find the file specified.
```

Inf2Cat stderr empty. Separate Windows CodeIntegrity event 3077 was the expected Smart App Control block of the **unsigned application before its launch**, not the cause of the Step 7 failure.

## Root cause

Frozen M138 and its M195 bootstrap extracted only `Inf2Cat.exe` from the pinned official `Microsoft.Windows.WDK.x64` NuGet package. The 28000 WDK's .NET Inf2Cat is **not a standalone executable**. It requires four same-directory Microsoft `UniversalStore.HardwareWorkflow` DLLs plus `WindowsProtectedFiles.xml`.

A dedicated Windows 2025 runner established this in two independent tests:

1. With the four DLLs, `Inf2Cat /?` exits 0 and prints help, but real `Inf2Cat /driver:... /os:10_GE_X64 /verbose` still exits -1 complaining that `WindowsProtectedFiles.xml` is missing.
2. After adding that exact XML from the same WDK package, the **real catalog-generation command** on a non-installing synthetic INF successfully writes the expected CAT and exits 0.

This is a **tool packaging defect**, not a fault in the authentic AMD source files, profile-specific INF or DAT, local signer or hardware GPU driver.

## Exact six-file WDK member contract

All bytes come from the **existing**, already-pinned and SHA-verified Microsoft WDK NuGet 10.0.28000.2526, archive SHA-256 `63C939FB5A79295BF40E941DB592681272219B04EDFF095FE2F3D123E5579A90`.

| Portable WDK x86 member under `c/bin/10.0.28000.0/x86/` | Bytes | SHA-256 |
|---|---:|---|
| `Inf2Cat.exe` | 34,880 | `82C302FC9069783674B51665CE80E769F8500DB7FC737A4B8A3773C521950B86` |
| `Microsoft.UniversalStore.HardwareWorkflow.Cabinets.dll` | 60,480 | `E967F7CBE45367A854A69FE7178CE374FAC570D2D1CFFFEA5525697890B8EB93` |
| `Microsoft.UniversalStore.HardwareWorkflow.Catalogs.dll` | 33,344 | `EB5C35941565E42FFCCB943969443E69A5A520D79D86E32E910B323BCDB41920` |
| `Microsoft.UniversalStore.HardwareWorkflow.InfReader.dll` | 60,480 | `9C14A7866F4E3EDFDA382E57A8E585462E69DE1F50DFFF6351D32934A6C1FF95` |
| `Microsoft.UniversalStore.HardwareWorkflow.SubmissionBuilder.dll` | 144,960 | `E8D61F87E041C5EF6E10408CABE344457A4607D75997965077810B5862C0B4B7` |
| `WindowsProtectedFiles.xml` | 180,884 | `B6DCF5D577C4EB96A63DBA8E8F952CC9A1B6E7C21C6C5784AEC82B261ABEC640` |

No WDK installation, global SDK setup, Test Mode, Secure Boot change, private key redistribution or broad driver mutation is introduced. Old portable-cache directories containing only `Inf2Cat.exe` are completed by extracting the missing five exact WDK members into the **same directory**, verifying length/hash before use; a changed existing file is never silently overwritten.

The bootstrap now invokes a **non-installing** Inf2Cat `/?` runtime check before returning the toolchain to the Step 7 worker, proving all six runtime files are present and that the executable can start.

## Verified Windows CI

- [Portable toolchain audit with actual synthetic catalog creation and SignTool smoke](https://github.com/DontDoMuch/Finesse/actions/runs/45287204796): passed. `M195_RUNTIME_PINNED_INF2CAT_PASS`, `M195_CLEAN_WINDOWS_REAL_BOOTSTRAP_SMOKE_PASS`, `M195_INF2CAT_SYNTHETIC_EXIT=0`, `M195_PINNED_INF2CAT_SYNTHETIC_CATALOG_PASS`, `M195_PINNED_SIGNTOOL_HELP_EXIT=0`, `M195_PINNED_SIGNTOOL_HELP_PASS`.
- [M195 offline compatibility tests and source audit](https://github.com/DontDoMuch/Finesse/actions/runs/45287216863): **374** tests passed, four tool builds, zero warnings/errors, original 194-hash AMD manifest retained and no redistributed proprietary AMD files.
- [Win64 single-file Finesse.exe build, checksum and attached unsigned test artifact](https://github.com/DontDoMuch/Finesse/actions/runs/45287216990): 116,801,806 bytes; SHA-256 `4F01A529B51072051AE29E81D0DD232BC02A9A7E85CE080B490D935F46719551`, `NotSigned`; attached hardware-test ZIP artifact ID `14871807532`.
- The existing GUI failure path fix (earlier M195 commit) also surfaces the actual failed step, message and user-accessible diagnostics under `Downloads\Finesse\Reports`, while preserving the original ProgramData session.

## Acceptance boundary

**This corrects the observed missing-tool root cause and confirms a working full portable Inf2Cat invocation in CI, not a completed AMD 26.11.1 physical install.** The exact source-driven 194-file AMD catalog, signing, 14/14 measured trust, bind, AMD MSI, planned restart, first postboot Code 0 and original-driver recovery still require the user's next real hardware test. Keep the draft PR open; leave frozen M194, public `main`, Secure Boot and test signing state unchanged. No public release or merge based solely on this smoke test.
