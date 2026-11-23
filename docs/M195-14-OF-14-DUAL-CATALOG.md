# M195 — real Driver Store 14/14 dual-catalog audit

**M195 draft source, not the production installer.** This round reopened the actual frozen `M194-26111-THREE-DEVICE-FULL-LIFECYCLE-SOURCE.zip` and verified SHA-256 `E7BEAB1C14F8451D0667733CAEFCBA03D284D722FAB178B100E510CF949E9AA1`. The primary references are:

- `PrivateM80/M191Validation.cs` and `PrivateM80/Assets/M191-Validation.ps1`
- `PrivateM80/M115DualTrustTransaction.cs` and `Assets/M115-V5-Catalog-Interop.ps1`
- `PrivateM80/Assets/M115-PINNED-CONTRACT.json`

The original contract specifies **14 exact original AMD graphics files**, `u0206030.cat` SHA-256 `786D0B7F596D367F2C71BADAA31068DD1A63C20E0C8C7DE94E8E23019EBAFE04`, and the optional database registration name `Finesse-AMD-26111-Official-WHCP.cat`.

## New source

- `M195DualCatalogCoverage.cs`: pure 14-file evaluator with four independently measured counters: original SignTool file membership, Finesse SignTool file membership, original Windows-registered catalog membership, local Windows-registered catalog membership. It also records original WHCP Authenticode, original `/kp` and local `/pa` signature policy and exact Driver Store INF/CAT hashes. A correct file SHA or a catalog merely present on disk cannot mark `DualTrustVerified=true`.
- `M195WindowsDriverStoreLocator.cs`: actual read-only `SetupGetInfDriverStoreLocationW`, mapping the published `oemNN.inf` to `DriverStore/FileRepository`. Requires the published, mapped, and candidate INF bytes to match and the mapped signed local CAT to have the expected hash.
- `M195WindowsDualCatalogAudit.cs`: Windows read-only M191 audit of an **actually assigned supported GPU** and its mapped Driver Store package. For all 14 unchanged AMD files it verifies SHA-256, local/official SignTool `/pa /c` membership, and registered matching-catalog SHA-256 membership through native `CryptCATAdmin`. Verifies original CAT `/kp`, original WHCP Authenticode signer, and local catalog `/pa`. Emits a JSON audit receipt with per-member results and unverified fields, without changing the driver or certificate stores.
- `M195OriginalAmdCatalogRegistration.cs`: **internal, not callable from the public GUI**. Re-audits before allowing original AMD catalog registration. Only if the original CAT identity, `/kp`, actual Driver Store target and all 14 original file memberships are verified, and Windows lacks original registration, it invokes `CryptCATAdminAddCatalog` under the exact frozen original name. Refuses different existing managed catalog bytes, never removes preexisting catalogs, and re-audits all 14 after registration. It must not confuse registration success with complete dual trust or target Code 0.

## Important lifecycle distinction

Frozen M191's dual trust and Lenovo extension inspection is an **evidence audit**, not a new unconditional install blocker. The optional original catalog registration remedy must be used only for the verified original WHCP catalog; lack of a complete audit should be reported accurately rather than silently treated as successful original/local trust.

The new native catalog audit requires that the exact `oemNN.inf` has become **assigned to the intended physical GPU** at the correct target driver version. It is *post-bind evidence*; a staged-but-unbound package is insufficient. A preboot Code 43 may be recorded as pending the planned reboot, but is **not** target Code 0.

M195 does **not** yet wire the native registration and auditing code to the live GUI/Step 8. The actual M179 `UpdateDriverForPlugAndPlayDevicesW` binding, scoped rollback, AMD Software and postboot work remain. No new M195 physical test, registration, catalog generation or driver bind has been performed. The source itself contains no AMD binary files or signing keys.

## Automated tests

Vendor-independent tests cover all 14 targets and four distinct counters, missing original registration, missing local/original file membership, wrong kernel-signing policy, wrong Driver Store catalog, 13/14/duplicate/tampered-file evidence, and exact bound target PnP INF/driver version/instance identity. On the real Windows runner, `SetupGetInfDriverStoreLocationW` maps a published OEM INF into the Driver Store without driver mutation.

Use the **globally installed** .NET 10 SDK:

```powershell
dotnet run --project Source/test/Finesse.Compatibility.OfflineTests/Finesse.Compatibility.OfflineTests.csproj -c Release
```

The CI source checks **do not** execute original registration or SignTool checks against the real proprietary AMD 26.11.1 package. All hardware/source authenticity tests still require separate evidence.
