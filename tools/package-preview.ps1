[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Year,
    [Parameter(Mandatory = $true)]
    [string]$Commit,
    [Parameter(Mandatory = $true)]
    [string]$SourceBuildDirectory,
    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory
)

$ErrorActionPreference = "Stop"

if ($Year -ne "2026") {
    throw "Civil 3D year '$Year' is unsupported; only 2026 is configured."
}
if ($Commit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "Commit must be the full 40-character Git commit SHA."
}
if (-not (Test-Path -LiteralPath $SourceBuildDirectory -PathType Container)) {
    throw "Source build directory does not exist: $SourceBuildDirectory"
}
if (Test-Path -LiteralPath $DestinationDirectory) {
    throw "Destination already exists: $DestinationDirectory"
}

$productDlls = @(
    "AutoGIS.Civil3D.Adapter.dll",
    "AutoGIS.Civil3D.Proposal.dll"
)
$actualDlls = @(Get-ChildItem -LiteralPath $SourceBuildDirectory -Filter *.dll -File | ForEach-Object Name)
$unexpectedDlls = @($actualDlls | Where-Object { $_ -notin $productDlls })
if ($unexpectedDlls.Count -ne 0) {
    throw "Unexpected DLL in preview build output: $($unexpectedDlls -join ', ')"
}
foreach ($name in $productDlls) {
    if (-not (Test-Path -LiteralPath (Join-Path $SourceBuildDirectory $name) -PathType Leaf)) {
        throw "Missing product DLL: $name"
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$smokeScript = Join-Path $repositoryRoot "scripts\new-proposal-smoke.scr"
$instructions = Join-Path $repositoryRoot "docs\new-proposal-preview.md"
foreach ($path in @($smokeScript, $instructions)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing preview handoff file: $path"
    }
}

New-Item -ItemType Directory -Path $DestinationDirectory | Out-Null
foreach ($name in $productDlls) {
    Copy-Item -LiteralPath (Join-Path $SourceBuildDirectory $name) -Destination (Join-Path $DestinationDirectory $name)
}
Copy-Item -LiteralPath $smokeScript -Destination (Join-Path $DestinationDirectory "new-proposal-smoke.scr")
Copy-Item -LiteralPath $instructions -Destination (Join-Path $DestinationDirectory "new-proposal-preview.md")

function Get-Sha256([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return [System.BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace("-", "").ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

$dllInventory = @($productDlls | ForEach-Object {
    $path = Join-Path $DestinationDirectory $_
    [ordered]@{
        path = $_
        sha256 = Get-Sha256 $path
    }
})
$buildInfo = [ordered]@{
    civil3dYear = $Year
    commit = $Commit.ToLowerInvariant()
    qualification = "Build-only preview; manual smoke evidence from licensed Civil 3D 2026 is required."
    productDlls = $dllInventory
}
$json = $buildInfo | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText(
    (Join-Path $DestinationDirectory "build-info.json"),
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false)
)

$expectedInventory = @(
    "AutoGIS.Civil3D.Adapter.dll",
    "AutoGIS.Civil3D.Proposal.dll",
    "build-info.json",
    "new-proposal-preview.md",
    "new-proposal-smoke.scr"
)
$actualInventory = @(Get-ChildItem -LiteralPath $DestinationDirectory -File | ForEach-Object Name | Sort-Object)
if ((Compare-Object $expectedInventory $actualInventory).Count -ne 0) {
    throw "Preview artifact inventory is not the expected product-only set."
}

$actualInventory
