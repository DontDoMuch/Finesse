# M195 live lifecycle bridge: frozen M182 ordering, source-driven entry point

This document is based on the **actual frozen M194 archive**, verified SHA-256:
`E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`.
The frozen source was inspected at:
`Source/src/Finesse.Gui/PrivateM80/M182InstallLifecycleWorkflow.cs`,
`M80Pipeline.cs`, `M80Recipe.cs`, `M175SigningOperation.cs`,
`M179StageBindOperation.cs`, and `M149CatalogGeneration.cs`.

**Everything currently in GitHub on the M195 branch is development work. No live installation has been performed with M195, and frozen M194 has not been edited.**

## Public 12-step lifecycle contract

`Source/src/Finesse.Compatibility/M195LifecycleSequence.cs` defines a C# interface for injecting the actual Windows operations into a source-first, full-lifecycle workflow. The coordinator imposes the same order the frozen GUI actually used:

| Step | M194-equivalent requirement |
|---|---|
| 1. Identify device | Exact PCI vendor/device/subsystem/revision and matching unique PnP device |
| 2. Inspect driver | Active original OEM INF, driver version and PnP problem code |
| 3. Backup original | Preserve existing working driver. A verified export is mandatory for Go S/Go 2 tester transactions |
| 4. Acquire AMD | Exact official AMD 26.11.1 EXE identity and real Windows Authenticode |
| 5. Extract source | Pinned original 194 files + separately preserved authentic AMD main catalog |
| 6. Prepare | Reconstruct supported Phoenix/Strix INF + positional DAT, verify every candidate SHA |
| 7. Generate catalog | Pinned Microsoft Inf2Cat, 194 unchanged expected package proofs and unsigned PKCS#7 catalog |
| 8. Sign / stage / bind | One live signing operation; actual Windows binding; 14/14 dual original/local catalog critical-file verification |
| 9. Verify preboot | Exact PnP GPU/INF/version; Code 43 may be **pending restart**, never a target success |
| 10. Install AMD Software | Native MSI verified and installed **before** postboot scheduling or restart |
| 11. Register + reboot once | Save complete transaction/resume receipt and request exactly one restart |
| 12. Verify postboot | First GPU observation after a distinct real boot; Code 0 + exact INF and AMD Software confirms target; recover baseline otherwise |

Two separate methods make the Windows boot boundary explicit: `RunPrebootAsync` returns a ticket, and `RunPostbootAsync` requires an actual boot-identity change before evaluating success. A recovery receipt cannot be described as target-driver success. No automatically scheduled second reboot.

The source contains an **interface and coordinator**, not real implemented `IWindowsInstallationActions` or `IPostbootActions` production classes. A fake adapter provides CI regression tests for step ordering, deliberate Code 43, MSI failure, 14/14 trust failure, backup failure, postboot recovery ordering and one reboot. **Passing these tests does not make a complete installer.**

## Source and offline Inf2Cat integration

`Amd26111TwoPhasePreparation` now verifies the pinned AMD installer and original file identities before creating `Amd26111CandidateRecipe` and `AmdSourceFirstStager` output.

`Amd26111UnsignedCatalogGenerator` is an offline port of the frozen `M149CatalogGeneration` boundary. It requires the exact Microsoft WDK `Inf2Cat.exe` SHA-256:

```text
82C302FC9069783674B51665CE80E769F8500DB7FC737A4B8A3773C521950B86
```

It revalidates the *entire* 194-file candidate against the public original source manifest plus only the two approved candidate hashes, runs the pinned tool, records output, confirms unchanged package files and original AMD catalog, and verifies the generated PKCS#7 SignedData has **zero signer infos**. This is **not** signing or Windows driver registration.

The offline `Finesse.OfficialSourcePreparation` tool has an optional `--inf2cat` argument. Its output remains **UNSIGNED** regardless of whether this optional catalog generation succeeds.

## Remaining production integration

The actual frozen `M182InstallLifecycleWorkflow` still calls `M80Recipe.LoadForM194` before AMD source acquisition. The live M175/M179 downstream adapters also expect the `M80Recipe` members and their private catalog receipt. To make M195 a genuine public GUI:

1. Publish the legally redistributable M194-derived Windows GUI + native signing/binding/rollback implementation, excluding AMD/Lenovo proprietary binaries and private scripts that are not licensed for publication.
2. Replace the M80 private recipe loader with the new source-only installer contract **and** a new source-driven candidate/manifest type through all call sites (not just the download call).
3. Build production implementations of `IWindowsInstallationActions` and `IPostbootActions` with exact WinVerifyTrust, backup, Inf2Cat, one live signing transaction, Win32 SetupAPI binding, dual-catalog trust and native MSI/postboot integrations.
4. Preserve the actual M194 12-step user-facing GUI with meaningful progress only. Store milestone names and technical diagnostics in evidence files.
5. Run full compiler/test suite on clean Windows using system-installed .NET 10, physical Secure Boot ON / Test Signing OFF Go 1 and Go 2 postboot testing, and real original AMD source verification. Go S physical validation remains pending.

**Do not** claim that an interface passing unit tests means the signing, binary driver, catalog database or AMD Software operations work in M195. The frozen physical Code 0 evidence applies to M193/M194, not M195.

The complete engineering checklist is maintained separately in the private master handoff document. Its requirements still govern all M195 changes.
