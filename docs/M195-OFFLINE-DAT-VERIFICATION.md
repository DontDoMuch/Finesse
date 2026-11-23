# Offline AMD DAT reproduction (M195)

This utility is **for developers and independent reviewers**, not for ordinary Finesse installation. It performs an offline file transformation and does not install drivers, change certificates, disable Secure Boot, require administrator rights, or contact AMD servers.

## Build prerequisites

Use the **system-installed .NET 10 SDK**, available as `dotnet` on Windows `PATH`. The repository `global.json` selects the installed .NET 10 feature band. The tool is independent of the main GUI and has **no bundled AMD files**.

```powershell
dotnet --info
dotnet build Source/tools/Finesse.DatVerifier/Finesse.DatVerifier.csproj -c Release
```

## Verify the previously tested AMD 26.11.1 transformation

Obtain `B026778\amdgcf.dat` **from your own verified official AMD 26.11.1 installer**, not from a random file-sharing download. The authentic AMD 26.11.1 original DAT must have SHA-256:

```text
2DC60D0B41488E504E78213B8DA1DF790E7E39AF67A892E9441F41642659E459
```

Point the tool at that file. **Example for Go 1** (substitute your actual extracted source path):

```powershell
dotnet run --project Source/tools/Finesse.DatVerifier/Finesse.DatVerifier.csproj -c Release -- `
  --input "C:\AMD\EXTRACTED\Packages\Drivers\Display\WT6A_INF\B026778\amdgcf.dat" `
  --output ".\dat-verification\Go1-amdgcf.dat" `
  --profile Go1 `
  --release "26.20.15.16-261109a-206030E-AMD-Software-Adrenalin-Edition" `
  --original-sha256 "2DC60D0B41488E504E78213B8DA1DF790E7E39AF67A892E9441F41642659E459" `
  --expected-sha256 "7986B5DC0CB3C2207ECE857D0060475A603B5342649D146874850C0D4A2509CA"
```

For Go S Z1 Extreme, select `--profile GoS` with the same expected final SHA. For Go 2 Z2 Extreme, use `--profile Go2` and expected SHA `B63E4F58C164950B41C07071B3CF6797B35ACC597B9476836FCA66E8A658E3FD`.

The tool validates the **original source SHA**, full DAT structure, original release-specific MetroHash64 checksum, exact profile, rebuilt 3-byte positional rows, final MetroHash checksum, and **expected final SHA** before writing anything. Existing output with different bytes is not overwritten.

These 26.11.1 expected digests were verified locally against frozen M193/M194 data. Offline compilation and synthetic tests can run in GitHub Actions without uploading any AMD driver bytes. The tool's live Windows CLI execution with a user's source DAT is a separate verification, not a claim of GPU installation success.

## Release-specific warning

The format and release marker must be **independently verified for each new AMD version**. Do not reuse these hashes, the expected record count, or companion-INF assumptions for a later release. The source of truth is the authenticated original AMD package plus the actual supported format. Any incompatible format must fail closed.

This standalone module does **not yet integrate with the full Finesse installer**. See [BUILDING.md](../BUILDING.md) for remaining source-publication blockers and [VERIFYING.md](../VERIFYING.md) for evidence distinctions.
