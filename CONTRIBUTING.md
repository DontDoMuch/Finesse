# Contributing to Finesse

Thank you for helping improve compatibility and transparency.

## What to contribute

Code, tests, hardware evidence with personal details removed, build reproducibility improvements, and docs are welcome. For implementation work, open an issue or pull request describing the exact AMD driver release, handheld model and hardware ID, expected behavior, observed behavior, and how to reproduce.

**Please do not upload** AMD/Lenovo proprietary driver packages, private keys or certificates with private keys, personal diagnostic ZIPs, sensitive logs, passwords, or unrelated third-party binaries.

## Engineering requirements

- Preserve frozen **M194**; all changes go into a new branch or version.
- Keep the README's ordinary end-user installation steps prominent. Move technical instructions to supporting documents.
- Use the **system-installed Microsoft .NET 10 SDK** exposed through `dotnet` on `PATH`, and repository-relative source paths; do not use a project-local/portable SDK, SDK auto-download, or a maintainer-specific installation directory. The delivered EXE remains self-contained for end users.
- For each release, verify the original AMD installer authenticity and rederive the INF and DAT transformations; do not reuse pinned 26.11.1 DAT header bytes.
- Retain the exact compatible hardware routing and per-profile policies.
- Keep the complete signing, **14/14 two-catalog coverage**, driver backup, scoped silent cleanup, verified AMD Software installation before **one planned reboot**, first postboot GPU observation, and rollback.
- Avoid PowerShell pitfalls: case-insensitive automatic variables (including `$HOME` / `$home`), assuming `$Matches` is safe scratch storage, accessing nonexistent properties, assuming `.Count` exists on any object, unsupported parameters, and ignoring native exit codes.
- For C#, check nullable reference contracts, local-variable scoping, compiler warnings, and target SDK compatibility.
- Treat offline/CI passing tests separately from actual Secure Boot-on, postboot Code 0 hardware results.

## Licensing

The repository currently uses the **PolyForm Noncommercial License 1.0.0**. It is source-available under noncommercial terms, **not an OSI-approved open-source license**. Do not assume the code can be commercially redistributed or relicense it without the owner's explicit decision.

See [BUILDING.md](BUILDING.md) for the public-source build roadmap and [VERIFYING.md](VERIFYING.md) for the verification policy.
