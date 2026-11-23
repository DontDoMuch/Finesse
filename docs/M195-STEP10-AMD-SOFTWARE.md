# M195 Step 10 — AMD Software MSI / Adrenalin integration (internal, unreleased)

**Current acceptance:** OFFLINE Windows compilation + synthetic MSI policy tests only. **No M195 physical AMD Software MSI execution, Store AppX removal, 1603 retry, reboot or postboot Code 0 is claimed.** Native code is internal and **not connected to the GUI or `M195WindowsPreparationActions.InstallVerifiedAmdSoftwareAsync`**. Public main, frozen M194 and published installers have not changed.

## Frozen source lineage and exact 26.11.1 identity

Actual source: `M194-26111-THREE-DEVICE-FULL-LIFECYCLE-SOURCE.zip`, SHA-256 `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`, from parent `M194-FROZEN-TESTED-BASELINE-20261122.zip` SHA-256 `FE85DFB799266FAB5F4C47E658A24232D828EF31EE7FC6EFA70EEB60F9592B9C`.

Read and compared the frozen `M140AdrenalinIntegration.cs`, `M141StoreSoftwareCleanup.cs`, `M182InstallLifecycleWorkflow.cs`, `M169-MSI-VERIFIED-CONTRACT.json` and `README-M184.md`. Relevant original evidence: M168 MSI identity and M183/M184 explicit `WIX_DOWNGRADE_DETECTED` conflict. Those frozen test results are **not** new M195 real-device evidence.

Pinned release-specific identities (M195 intentionally does **not** claim to support newer AMD versions):

| Source component | Identity |
| --- | --- |
| Authenticated AMD official installer | AMD 26.11.1, exact original SHA-256 from `OfficialAmd26111SourceContract` |
| Original CCC2 NSIS executable | `B026778/ccc2_install.exe`, 246,780,968 bytes, SHA-256 `4E3437C2DFF51B6B72A814A010F77679B273A8C355AB849BAD836206FB9B3915` |
| Original nested AMD Settings MSI | `CN/cnext/cnext64/ccc-next64.msi`, 155,561,984 bytes, SHA-256 `B1AFF2C1494877E15E23025D6B09F898E8E422777DA00CA013695181D7F116F2` |
| Target MSI ProductCode | `{9E860308-0949-4D1D-BABD-980EEC7CE8CD}` |
| AMD Settings UpgradeCode | `{A837E2B7-5BA6-4286-BC21-A002FCDD9A85}` |
| MSI ProductName | `AMD Settings` |
| Target ProductVersion metadata | `2026.1109.2249.2112` |
| Target Windows display driver | `32.0.32015.16001` |
| MSI publisher | `Advanced Micro Devices` |

## New source modules

- `M195AmdSettings26111.cs`: independent release pins, pure M184 downgrade log parser, native related-product blocker approval, MSI success status and tested silent `/qn /norestart` command args
- `M195WindowsAmdSettingsMsiIdentity.cs`: read-only native MSI database Property queries (`MsiOpenDatabaseW`, SQL view/fetch, `MsiRecordGetStringW`); exact ProductCode, UpgradeCode, ProductName, ProductVersion and Manufacturer checks; native installed product family enumeration via `MsiEnumRelatedProductsW` and `MsiQueryProductStateW`
- `M195AmdSettingsMsiPreparation.cs`: independently reverify official AMD 26.11.1 source and pinned original 194-file extraction; hash and authenticate the original CCC2; extract portable `7z.exe` / `7z.dll` from the pinned official 7-Zip 26.06 SFX using pinned `7zr.exe`; extract only the nested MSI + two manifest JSON files; verify exact MSI bytes, WinVerifyTrust, native property table and matching driver/software manifest
- `M195WindowsStoreAmdSettingsCleanup.cs`: remove only current-user, exact-name `AdvancedMicroDevicesInc-2.AMDRadeonSoftware` and `AdvancedMicroDevicesInc-2.AMDSoftware` AppX packages; verify removal; log warning and continue on unrelated Store-service errors. Does not touch other apps, other accounts or provisioning
- `M195WindowsAmdSettingsInstall.cs`: source-authenticated AMD Settings **MSI FIRST**, silent `/i <exact-verified-msi> /qn /norestart /L*V <fresh-log>`; preserve assigned GPU instance, INF, driver version and preboot Code 0/43; native MSI product-and-frontend verification, 0/3010 outcomes and full transaction/evidence receipts
- `M195WindowsStep10Native.cs`: internal-only single Step10 entry point combining verified source/preparation and native installation. It is not yet callable from the public installer

## Strict M184 fallback

1. **Never pre-uninstall desktop AMD Settings merely due to a version comparison.** MSI ProductVersion Upgrade-table ordering does not always correspond to a four-field dotted string.
2. Launch the authenticated target MSI once, noninteractive, without restarting; preserve its full verbose log and process exit code.
3. Consider a fallback **only on exactly exit 1603** with its own freshly generated MSI log showing `FindRelatedProducts`, a failed `LaunchConditions` action and a unique, valid `WIX_DOWNGRADE_DETECTED` GUID list. Any other 1603 is simply a failed install with preserved evidence.
4. Cross-check every proposed blocker against live `MsiEnumRelatedProductsW` under the pinned UpgradeCode, installed state, exact `AMD Settings` product name, AMD publisher, version and GUID. No foreign or stale product is removed.
5. Check current GPU binding **before and after** any narrowly scoped `/x <blocker-product-guid> /qn /norestart` uninstall. Require native removal confirmation; preserve uninstall logs. Only then retry the authentic original target MSI **once**. Never remove arbitrary AMD drivers, OEM extensions, original certificates or other MSI products.
6. Do not terminate a still-running MSI on timeout/cancellation. Completion is UNKNOWN; do not immediately retry, roll forward or reboot. Evidence records this status.
7. After MSI, re-observe the exact GPU and verify **both** native target product registration and the `RadeonSoftware.exe` frontend. Preboot Code 43 remains provisional. Exit 3010 is a **deferred** Windows restart request; Step 10 itself never restarts Windows.

No VC++ redistributable is automatically installed, prompted or required. No anti-cheat or overlay validation is an extra installation blocker. Technical MSI and AppX statuses are evidence data; later GUI progress must remain user-friendly.

## Tests and explicit remaining work

The GitHub Windows offline suite contains positive and negative regression cases for exact frozen MSI pins, ProductCode/UpgradeCode/publisher/name/version mismatches, genuine vs unrelated 1603 logs, malformed/duplicated GUIDs, foreign/uninstalled blockers, target-uninstall refusal, silent MSI command flags and native read-only MSI enumeration. The build-only suite does not execute the real AMD MSI or mutate the hosted Windows runner.

Important outstanding integrations:

- The portable full 7-Zip release is currently a caller-supplied **pinned** input, as is `7zr.exe`; the final EXE must **automatically obtain/verify** these dependencies using the frozen trusted source rules without asking users to install tools
- The native Step10 methods are disconnected from `M195WindowsPreparationActions` until Step 11 durable postboot registration, one planned reboot, first postboot Code 0 observation and recovery are implemented and fully integrated
- No physical proof yet that `/qn` eliminates all AMD/MSI dialogs in the relevant driver context, that M195 can extract the actual proprietary source successfully, or that Store cleanup and the M184 MSI fallback work on all three devices
- Preserve full M193/M194 per-profile INF + DAT positional records/MetroHash64, both 14-file catalog trust checks, scoped certificate lifecycle and exact recovery on every new build
- A successful Windows compile, a synthetic 1603 test and read-only MSI P/Invoke smoke test are **not** a working Finesse release

**M195 release status:** `BUILD_ONLY / INTERNAL_STEP10_NOT_LIVE_VALIDATED`. Do not merge or publish as an installer until the complete 12-step lifecycle, source-closure and real-device verification have been exercised.
