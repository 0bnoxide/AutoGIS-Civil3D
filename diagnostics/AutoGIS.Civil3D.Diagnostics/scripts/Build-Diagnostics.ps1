[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [string]$Civil3DRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$utilitiesPath = Join-Path $PSScriptRoot "Utilities.ps1"
if (-not (Test-Path -LiteralPath $utilitiesPath -PathType Leaf)) {
    throw "The shared script utilities are missing: $utilitiesPath"
}
. $utilitiesPath

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\AutoGIS.Civil3D.Diagnostics\AutoGIS.Civil3D.Diagnostics.csproj"
$bundleOutput = Join-Path $repositoryRoot "bundle\AutoGIS.Civil3D.Diagnostics.bundle\Contents\2025"

function Resolve-Civil3DRoot {
    param([string]$RequestedRoot)

    $candidates = @(
        $RequestedRoot,
        $env:CIVIL3D_2025_ROOT,
        (Join-Path $env:ProgramFiles "Autodesk\AutoCAD 2025")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Container) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    throw @"
Civil 3D 2025 was not found. Pass the installation root explicitly, for example:
  .\scripts\Build-Diagnostics.ps1 -Civil3DRoot "C:\Program Files\Autodesk\AutoCAD 2025"
You can also set CIVIL3D_2025_ROOT for the current shell.
"@
}

function Find-ManagedAssembly {
    param(
        [string]$Root,
        [string]$Name,
        [string[]]$PreferredSubdirectories = @(""),
        [switch]$AllowRecursiveFallback
    )

    $preferred = foreach ($subdirectory in $PreferredSubdirectories) {
        if ([string]::IsNullOrWhiteSpace($subdirectory)) {
            Join-Path $Root $Name
        }
        else {
            Join-Path (Join-Path $Root $subdirectory) $Name
        }
    }

    foreach ($candidate in $preferred) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    if (-not $AllowRecursiveFallback) {
        $checked = $preferred -join [Environment]::NewLine
        throw "Required Autodesk assembly '$Name' was not found in the expected Civil 3D 2025 locations:`n$checked"
    }

    $match = Get-ChildItem -LiteralPath $Root -Filter $Name -File -Recurse -ErrorAction SilentlyContinue |
        Sort-Object FullName |
        Select-Object -First 1

    if ($null -eq $match) {
        throw "Required Autodesk assembly '$Name' was not found beneath '$Root'."
    }

    return $match.FullName
}

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw "The .NET SDK was not found. Ask IT to install the Microsoft .NET 8 SDK, then run this script again."
}

$sdkList = & $dotnetCommand.Source --list-sdks
if (-not ($sdkList -match '^8\.')) {
    throw "A .NET 8 SDK is required. Installed SDKs:`n$($sdkList -join [Environment]::NewLine)"
}

$resolvedRoot = Resolve-Civil3DRoot -RequestedRoot $Civil3DRoot
Write-Host "Civil 3D root: $resolvedRoot"

$references = @{
    AcCoreMgdPath  = Find-ManagedAssembly -Root $resolvedRoot -Name "AcCoreMgd.dll" -PreferredSubdirectories @("")
    AcDbMgdPath    = Find-ManagedAssembly -Root $resolvedRoot -Name "AcDbMgd.dll" -PreferredSubdirectories @("")
    AcMgdPath      = Find-ManagedAssembly -Root $resolvedRoot -Name "AcMgd.dll" -PreferredSubdirectories @("")
    AecBaseMgdPath = Find-ManagedAssembly -Root $resolvedRoot -Name "AecBaseMgd.dll" -PreferredSubdirectories @("ACA", "") -AllowRecursiveFallback
    AeccDbMgdPath  = Find-ManagedAssembly -Root $resolvedRoot -Name "AeccDbMgd.dll" -PreferredSubdirectories @("C3D", "") -AllowRecursiveFallback
}

$expectedAssemblySeries = @{
    AcCoreMgdPath = @(25, 0)
    AcDbMgdPath   = @(25, 0)
    AcMgdPath     = @(25, 0)
    AeccDbMgdPath = @(13, 7)
}

foreach ($entry in $expectedAssemblySeries.GetEnumerator()) {
    $assemblyPath = $references[$entry.Name]
    $actualVersion = [Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version
    $expectedMajor = $entry.Value[0]
    $expectedMinor = $entry.Value[1]

    if ($actualVersion.Major -ne $expectedMajor -or $actualVersion.Minor -ne $expectedMinor) {
        throw "'$assemblyPath' is assembly version $actualVersion; expected Civil 3D 2025 series $expectedMajor.$expectedMinor. Refusing a cross-version build."
    }
}

Write-Host "Resolved Autodesk references:"
foreach ($entry in $references.GetEnumerator() | Sort-Object Name) {
    $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($entry.Value).Version
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($entry.Value).FileVersion
    Write-Host "  $($entry.Name): $($entry.Value) [assembly $assemblyVersion; file $fileVersion]"
}

$buildArguments = @(
    "build",
    $projectPath,
    "--configuration",
    $Configuration,
    "--nologo"
)

foreach ($entry in $references.GetEnumerator()) {
    $buildArguments += "/p:$($entry.Name)=$($entry.Value)"
}

& $dotnetCommand.Source @buildArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

$compiledDll = Join-Path $repositoryRoot "src\AutoGIS.Civil3D.Diagnostics\bin\$Configuration\net8.0-windows\AutoGIS.Civil3D.Diagnostics.dll"
if (-not (Test-Path -LiteralPath $compiledDll -PathType Leaf)) {
    throw "The build reported success but the expected DLL was not found: $compiledDll"
}

New-Item -ItemType Directory -Path $bundleOutput -Force | Out-Null
Copy-Item -LiteralPath $compiledDll -Destination $bundleOutput -Force
$bundledDll = Join-Path $bundleOutput "AutoGIS.Civil3D.Diagnostics.dll"

$compiledHash = Get-AutoGISFileSha256 -LiteralPath $compiledDll
$bundledHash = Get-AutoGISFileSha256 -LiteralPath $bundledDll
if ($compiledHash -ne $bundledHash) {
    throw "The bundle DLL hash does not match the compiled DLL. Refusing to report a successful build."
}

$pdbPath = [System.IO.Path]::ChangeExtension($compiledDll, ".pdb")
if (Test-Path -LiteralPath $pdbPath -PathType Leaf) {
    Copy-Item -LiteralPath $pdbPath -Destination $bundleOutput -Force
}

Write-Host ""
Write-Host "Build complete."
Write-Host "DLL: $compiledDll"
Write-Host "SHA256: $compiledHash"
Write-Host "Bundle: $(Join-Path $repositoryRoot 'bundle\AutoGIS.Civil3D.Diagnostics.bundle')"
Write-Host "Next: test the DLL with NETLOAD, then run install-current-user.cmd if approved."
