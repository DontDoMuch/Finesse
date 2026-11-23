# M195 Windows-native production adapter — Steps 1–7 and 9

**Development implementation only.** Frozen M194 was reopened from `M194-26111-THREE-DEVICE-FULL-LIFECYCLE-SOURCE.zip` and its SHA-256 verified:

```text
E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1
```

The concrete source was checked against these actual M194 files:

- `Source/src/Finesse.Platform.Windows/Drivers/WindowsSignedDriverSource.cs`
- `Source/src/Finesse.Platform.Windows/Drivers/WindowsDriverDeviceRecord.cs`
- `Source/src/Finesse.Platform.Windows/Drivers/PnpUtilDriverPackageExporter.cs`
- `Source/src/Finesse.Gui/PrivateM80/M147HardwareProfiles.cs`
- `Source/src/Finesse.Gui/PrivateM80/M171MinimalWorkflow.cs`
- `Source/src/Finesse.Gui/PrivateM80/M182InstallLifecycleWorkflow.cs`
- `Source/src/Finesse.Gui/PrivateM80/M179StageBindOperation.cs`

No AMD-owned binary, proprietary INF/DAT, catalog, private key, or private-recipe ZIP is included in this public code.

## New real Windows implementations

| Source file | Actual operation | Limit |
|---|---|---|
| `M195WindowsGpuInventory.cs` | Native SetupAPI + CfgMgr enumeration of present Display devices, exact hardware profile, active OEM INF, version, service, problem code | Read-only; returns no supported match on ordinary Windows runner |
| `M195WindowsRecoveryBaseline.cs` | Validates healthy assigned OEM driver; `pnputil /export-driver` only; verified per-file SHA-256 recovery export and evidence | No rollback/bind operation; not yet executed on an actual Go with M195 |
| `M195WindowsPreparationActions.cs` | Concrete `IWindowsInstallationActions` adapter for Steps **1–7**: hardware, current driver, backup, source authentication, extraction, candidate preparation, unsigned catalog | Only *preparation*; Step 8/10–12 deliberately throw `NotSupportedException` |
| `M195WindowsBindIdentity.cs` | Exact proposed INF hardware mapping, real observed PnP hardware ID and `oem##.inf` name validation from frozen M179 | Read-only pre-bind check, does **not** call SetupCopyOEMInf / NewDev |
| `M195WindowsPrebootObservation.cs` | Native read-only Step **9** PnP INF/version/problem-code observation; exact target Code 43 = provisional pending reboot | Does not claim postboot success; requires future real Step 8 binder |

### Go 1 hardware compatibility exception

Frozen M194's M147 identification used the PCI device/subsystem **revisionless identity** to find candidates, while its M182 workflow required full exact revision/subsystem evidence for the Go S Z1 Extreme and Go 2 Z2 Extreme profiles. M195 retains a narrow Go 1 revisionless HardwareIds fallback **only when no contradictory explicit revision appears**. Go S/Go 2 require exact full IDs. No unknown subsystem, explicit wrong revision, alternate AMD device, duplicate matching GPU, or ambiguous multiple-profile selection is accepted.

## Regression coverage

The Windows CI suite checks synthetic exact IDs for all profiles, Go 1 revisionless compatibility, wrong revisions and duplicate matches, native display enumeration on a Windows runner, recovery eligibility, export refusal for Code 43, missing/incorrect INF mappings, pre-reboot provisional Code 43/Code 0/wrong INF/wrong version, and explicit refusal to perform unported signing/MSI/reboot operations.

**Proven by CI:** C# compilation, synthetic behavioral tests, and read-only SetupAPI enumeration on GitHub-hosted Windows.

**Not proven by CI:** `pnputil /export-driver` successfully exporting a real Legion Go baseline; original AMD package acquisition on physical hardware; Inf2Cat on the real modified package; trust/sign/bind, AMD Software, a real reboot and Code 0; functioning end-user GUI.

## Remaining production implementation

The next high-priority source migration is M194's native M179 Step 8 signing/staging/binding, with its single live signer, preserved original AMD catalog, full 14/14 dual catalog coverage, scoped cleanup, backup-aware rollback, and actual staged INF hash verification.

Then port the exact M140 AMD Software MSI lifecycle (including the verified scoped 1603/WIX downgrade fallback), Step 11 single-restart registration, and Step 12 first postboot observation/recovery.

**Do not present this preparation adapter or the existing offline tools as a public installer.** The M194 `FINESSE_PRIVATE_LAB` gate and full source-publication refactor are not yet complete. The current `main` branch and frozen M194 binaries remain unchanged.

## Developer compilation

Use only the normal **globally installed .NET 10 SDK** discovered through `dotnet` on `PATH`:

```powershell
dotnet run --project Source/test/Finesse.Compatibility.OfflineTests/Finesse.Compatibility.OfflineTests.csproj -c Release
```

This is not an instruction to run an installer or change the active GPU.
