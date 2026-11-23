#Requires -Version 5.1
<#
M195 developer/CI build entry point.
Uses the system-installed .NET 10 CLI from PATH (no portable SDK bootstrap).
Refuses to call a private-lab-only or AMD-binary-dependent source tree a public build.
This does not install drivers or change machine trust.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release',
    [switch]$Ci
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'Source'
$guiProject = Join-Path $sourceRoot 'src\Finesse.Gui\Finesse.Gui.csproj'
$programFile = Join-Path $sourceRoot 'src\Finesse.Gui\Program.cs'
$dist = Join-Path $repoRoot 'dist'

function Invoke-Dotnet {
    param([Parameter(Mandatory=$true)][string[]]$ArgsToRun)
    & $script:dotnetExe @ArgsToRun
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw ("dotnet {0} failed with exit code {1}." -f ($ArgsToRun -join ' '), $exitCode)
    }
}

Write-Host '[M195] Checking system-installed .NET 10 SDK...'
$resolved = Get-Command 'dotnet' -CommandType Application -ErrorAction Stop
$dotnetExe = $resolved.Source
if (-not (Test-Path -LiteralPath $dotnetExe -PathType Leaf)) {
    throw 'System dotnet CLI could not be resolved to an executable.'
}
if (-not (Test-Path -LiteralPath $sourceRoot -PathType Container)) {
    throw 'M195_PUBLIC_SOURCE_NOT_PUBLISHED: Source directory is missing. The repository is not yet publicly buildable. No EXE was produced.'
}
if (-not (Test-Path -LiteralPath $guiProject -PathType Leaf) -or
    -not (Test-Path -LiteralPath $programFile -PathType Leaf)) {
    throw 'M195_INCOMPLETE_SOURCE: Full public Finesse.Gui project and Program.cs are required. No EXE was produced.'
}

Push-Location $sourceRoot
try {
    $sdk = (& $dotnetExe --version | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.') {
        throw ("M195_REQUIRE_SYSTEM_NET10: dotnet selected SDK '{0}', expected .NET 10. Install the Microsoft .NET 10 SDK globally and ensure dotnet is on PATH." -f $sdk)
    }
    Write-Host ("[M195] Selected .NET SDK: {0}" -f $sdk)
    $guiContent = Get-Content -LiteralPath $guiProject -Raw
    $entryContent = Get-Content -LiteralPath $programFile -Raw
    if ($guiContent -match 'M79-26111-PRIVATE-RECIPE-INPUT\.zip|M147/GoS-u0206030\.inf|M193-Go1-amdgcf\.dat') {
        throw 'M195_PRIVATE_AMD_ASSET_REFERENCES: Remove proprietary embedded resources and replace with a verified official AMD source acquisition path before public build.'
    }
    if ($entryContent -match 'M182 private lifecycle test only\.|FINESSE_PRIVATE_LAB_ONLY_FOR_INSTALL') {
        throw 'M195_PRIVATE_INSTALLER_GATE: Normal public Finesse launch path is not yet functional.'
    }
    if (Test-Path -LiteralPath (Join-Path $sourceRoot 'src\Finesse.Gui\PrivateM80\Assets\M79-26111-PRIVATE-RECIPE-INPUT.zip')) {
        throw 'M195_PRIVATE_AMD_PAYLOAD_PRESENT: Do not compile or publish this private AMD-derived recipe archive.'
    }

    Write-Host '[M195] Restoring GUI dependencies using system dotnet...'
    Invoke-Dotnet -ArgsToRun @('restore', $guiProject, '--locked-mode')

    # A proper public build must retain the proven profile, INF/DAT and lifecycle
    # regression harnesses. Never quietly skip one because its project is missing.
    $requiredHarnesses = @(
        'M166.ReleaseHarness',
        'M167.HardwareHarness',
        'M169.MsiHarness',
        'M175.SigningHarness',
        'M179.StageBindHarness',
        'M182.LifecycleHarness',
        'M190.CopyInfHarness',
        'M191.ConsolidationHarness',
        'M192.ScopedCleanupHarness',
        'M194.MultiProfileHarness'
    )
    foreach ($name in $requiredHarnesses) {
        $harness = Join-Path $sourceRoot ("test\{0}\{0}.csproj" -f $name)
        if (-not (Test-Path -LiteralPath $harness -PathType Leaf)) {
            throw ("M195_MISSING_MANDATORY_TEST: {0}" -f $name)
        }
        Write-Host ("[M195] Testing {0}..." -f $name)
        Invoke-Dotnet -ArgsToRun @('run', '--project', $harness, '--configuration',
            $Configuration, '--no-launch-profile')
    }

    Write-Host '[M195] Publishing user-facing Windows x64 executable...'
    if (-not (Test-Path -LiteralPath $dist -PathType Container)) {
        New-Item -ItemType Directory -Path $dist | Out-Null
    }
    Invoke-Dotnet -ArgsToRun @('publish', $guiProject, '--configuration', $Configuration,
        '--runtime', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:PublishTrimmed=false',
        '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=none',
        '--output', $dist)

    $exe = Join-Path $dist 'Finesse.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw 'M195_NO_EXE: .NET publish did not produce dist\Finesse.exe.'
    }
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToUpperInvariant()
    $manifest = Join-Path $dist 'SHA256SUMS.txt'
    [System.IO.File]::WriteAllText($manifest, ($hash + '  Finesse.exe' + [Environment]::NewLine),
        (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ("M195_PUBLIC_BUILD_PASS SHA256={0}" -f $hash)
    Write-Host ("Output: {0}" -f $exe)
}
finally {
    Pop-Location
}
