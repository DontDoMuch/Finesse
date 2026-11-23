# Verifying Finesse builds and releases

**Transparency requires actual evidence, not a badge alone.** A green GitHub build does not prove that Windows will initialize an AMD GPU or that a program is trusted by Microsoft.

## Current status

The repository has not yet published a complete, independently buildable installer or an attested GitHub Actions release. Do not treat historical local build receipts as GitHub CI proof.

The frozen engineering checkpoint is **M194 (AMD 26.11.1)**. Historical live test evidence supports:
- **Legion Go 1 / Z1 Extreme:** M193 postboot Code 0 and AMD Software installed (M194 not independently tested on this model).
- **Legion Go 2 / Z2 Extreme:** M194 postboot Code 0, target driver retained, no rollback.
- **Legion Go S / Z1 Extreme:** profile and offline tests exist; **live success not established**.

All claims are tied to an actual version, hardware profile, and evidence set; they are not blanket claims for future AMD drivers.

## Future verification process

Once CI/CD is implemented and publishing is enabled, the GitHub Release should include:
1. `Finesse.exe`, SHA-256 checksum, tagged source commit, and build/test logs.
2. GitHub artifact attestation that associates the exact downloadable EXE with its build workflow and source commit.
3. A clear distinction between automated CI checks and any physical hardware test reports.
4. Version-specific compatibility recipe details, including exact INF changes and decoded/rebuilt DAT verification.

For a published, attested release, install GitHub CLI and run (substitute the **actual** downloaded release path):

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\Finesse.exe'
gh attestation verify '.\Finesse.exe' -R DontDoMuch/Finesse
```

Only use the attestation command after the release actually has an attestation. A provenance attestation does **not** replace Microsoft Authenticode signing or validate the runtime behavior of GPU drivers.

**Authenticity:** Download from the official GitHub repository. Verify publisher signatures of official AMD installers independently; Finesse's locally generated driver catalog does not make AMD officially support the device.

## Security warnings

Windows Smart App Control, Microsoft Defender, and third-party antivirus software may flag unsigned or unfamiliar applications. **Do not assume every warning is a false positive.** Confirm the source, hashes, provenance and detection details; do not disable security protections merely to proceed.

Finesse is independent and not affiliated with AMD or Lenovo.
