#Requires -Version 5.1
<#
M195 public-source publication audit; read-only, runs on Windows PowerShell.
It checks that we don't accidentally commit vendor binaries, private driver
recipe archives, signing private keys or a GUI project enabled by the private
milestone compile flag. Never changes repository files.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = $PSScriptRoot
$source = Join-Path $repository 'Source'
if (-not (Test-Path -LiteralPath $source -PathType Container)) {
    throw 'M195_PUBLIC_SOURCE_DIRECTORY_MISSING'
}
$forbiddenExtensions = @(
    '.inf', '.dat', '.cat', '.sys', '.dll', '.exe', '.msi', '.msix',
    '.zip', '.7z', '.pfx', '.p12', '.p7b', '.p7c', '.p8', '.p12',
    '.pvk', '.key', '.snk'
)
$forbiddenNames = @(
    'prod.keys', 'title.keys', 'amd-26111-proprietary-recipe.zip',
    'M79-26111-PRIVATE-RECIPE-INPUT.zip'
)
$files = @(Get-ChildItem -LiteralPath $source -File -Recurse -Force -ErrorAction Stop |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' })
$errorsFound = New-Object 'System.Collections.Generic.List[string]'
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart([char[]]@('\','/'))
    $extension = $file.Extension.ToLowerInvariant()
    if ($forbiddenExtensions -contains $extension -or
        $forbiddenNames -contains $file.Name) {
        $errorsFound.Add("PROHIBITED_VENDOR_OR_PRIVATE_FILE: $relative")
        continue
    }
    if ($file.Extension -ieq '.csproj' -or $file.Extension -ieq '.props' -or
        $file.Extension -ieq '.targets') {
        $projectContents = Get-Content -LiteralPath $file.FullName -Raw
        if ($projectContents -match 'FINESSE_PRIVATE_LAB|M79-26111-PRIVATE-RECIPE-INPUT|PrivateM80[\\/]+Assets') {
            $errorsFound.Add("PRIVATE_LAB_BUILD_REFERENCE: $relative")
        }
    }
}
if ($errorsFound.Count -gt 0) {
    foreach ($problem in $errorsFound) { Write-Host $problem }
    throw ("M195_PUBLIC_SOURCE_AUDIT_FAILED: {0} issues" -f $errorsFound.Count)
}
$manifest = Join-Path $source 'contracts\releases\amd-26.11.1-original-194.sha256'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw 'M195_194_LINE_MANIFEST_MISSING'
}
$rows = @(Get-Content -LiteralPath $manifest)
if ($rows.Count -ne 194) {
    throw ("M195_SOURCE_METADATA_BAD_ROW_COUNT: {0}" -f $rows.Count)
}
foreach ($row in $rows) {
    if ($row -cnotmatch '^[0-9A-F]{64}  [A-Za-z0-9_./-]+$') {
        throw 'M195_SOURCE_METADATA_INVALID_ROW_FORMAT'
    }
}
Write-Host ("M195_PUBLIC_SOURCE_AUDIT_PASS files={0} originalHashes=194 privateVendorBinaries=0" -f $files.Count)
