[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$utilitiesPath = Join-Path $PSScriptRoot "Utilities.ps1"
if (-not (Test-Path -LiteralPath $utilitiesPath -PathType Leaf)) {
    throw "The shared script utilities are missing: $utilitiesPath"
}
. $utilitiesPath

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceBundle = Join-Path $repositoryRoot "bundle\AutoGIS.Civil3D.Diagnostics.bundle"
$sourceDll = Join-Path $sourceBundle "Contents\2025\AutoGIS.Civil3D.Diagnostics.dll"
$sourceManifest = Join-Path $sourceBundle "PackageContents.xml"

if (-not (Test-Path -LiteralPath $sourceDll -PathType Leaf)) {
    throw "The compiled DLL is missing. Run build.cmd before installing."
}
if (-not (Test-Path -LiteralPath $sourceManifest -PathType Leaf)) {
    throw "The bundle manifest is missing: $sourceManifest"
}
Assert-AutoGISDirectoryTreeHasNoReparsePoints -LiteralPath $sourceBundle -Label "Source bundle"

if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
    throw "APPDATA is not defined for the current Windows user."
}

if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    throw "LOCALAPPDATA is not defined for the current Windows user."
}
if (-not [System.IO.Path]::IsPathRooted($env:APPDATA)) {
    throw "APPDATA must be an absolute path."
}
if (-not [System.IO.Path]::IsPathRooted($env:LOCALAPPDATA)) {
    throw "LOCALAPPDATA must be an absolute path."
}

$appDataRoot = [System.IO.Path]::GetFullPath($env:APPDATA)
$localAppDataRoot = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
$applicationPlugins = Join-Path $appDataRoot "Autodesk\ApplicationPlugins"
$targetBundle = Join-Path $applicationPlugins "AutoGIS.Civil3D.Diagnostics.bundle"
$backupRoot = Join-Path $localAppDataRoot "AutoGIS\PluginBackups"

New-Item -ItemType Directory -Path $applicationPlugins -Force | Out-Null
Assert-AutoGISDirectoryNotReparsePoint -LiteralPath $applicationPlugins -Label "ApplicationPlugins directory"

if (Test-Path -LiteralPath $targetBundle) {
    Assert-AutoGISDirectoryTreeHasNoReparsePoints -LiteralPath $targetBundle -Label "Installed bundle"
}
if (Test-Path -LiteralPath $backupRoot) {
    Assert-AutoGISDirectoryNotReparsePoint -LiteralPath $backupRoot -Label "Backup directory"
}

$sourceDllHash = Get-AutoGISFileSha256 -LiteralPath $sourceDll
$sourceManifestHash = Get-AutoGISFileSha256 -LiteralPath $sourceManifest
$stagingBundle = New-AutoGISUniqueChildPath -Parent $applicationPlugins -Stem ".AutoGIS.Civil3D.Diagnostics.bundle-staging"
$previousBundleBackup = $null
$activated = $false

try {
    Copy-Item -LiteralPath $sourceBundle -Destination $stagingBundle -Recurse
    Assert-AutoGISDirectoryTreeHasNoReparsePoints -LiteralPath $stagingBundle -Label "Staged bundle"

    $stagedDll = Join-Path $stagingBundle "Contents\2025\AutoGIS.Civil3D.Diagnostics.dll"
    $stagedManifest = Join-Path $stagingBundle "PackageContents.xml"
    if (-not (Test-Path -LiteralPath $stagedDll -PathType Leaf)) {
        throw "Staging did not produce the expected DLL: $stagedDll"
    }
    if (-not (Test-Path -LiteralPath $stagedManifest -PathType Leaf)) {
        throw "Staging did not produce the expected manifest: $stagedManifest"
    }
    if ((Get-AutoGISFileSha256 -LiteralPath $stagedDll) -ne $sourceDllHash) {
        throw "The staged DLL hash does not match the source DLL. The installed bundle was not changed."
    }
    if ((Get-AutoGISFileSha256 -LiteralPath $stagedManifest) -ne $sourceManifestHash) {
        throw "The staged manifest hash does not match the source manifest. The installed bundle was not changed."
    }

    if (Test-Path -LiteralPath $targetBundle) {
        New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
        Assert-AutoGISDirectoryNotReparsePoint -LiteralPath $backupRoot -Label "Backup directory"
        $candidateBackup = New-AutoGISUniqueChildPath -Parent $backupRoot -Stem "AutoGIS.Civil3D.Diagnostics.bundle"
        Move-Item -LiteralPath $targetBundle -Destination $candidateBackup
        $previousBundleBackup = $candidateBackup
    }

    Move-Item -LiteralPath $stagingBundle -Destination $targetBundle
    $activated = $true

    $installedDll = Join-Path $targetBundle "Contents\2025\AutoGIS.Civil3D.Diagnostics.dll"
    $installedManifest = Join-Path $targetBundle "PackageContents.xml"
    if ((Get-AutoGISFileSha256 -LiteralPath $installedDll) -ne $sourceDllHash) {
        throw "The installed DLL hash does not match the source DLL."
    }
    if ((Get-AutoGISFileSha256 -LiteralPath $installedManifest) -ne $sourceManifestHash) {
        throw "The installed manifest hash does not match the source manifest."
    }
}
catch {
    $installFailure = $_
    $rollbackFailures = New-Object 'System.Collections.Generic.List[string]'

    if ($activated -and (Test-Path -LiteralPath $targetBundle)) {
        try {
            Assert-AutoGISDirectoryTreeHasNoReparsePoints -LiteralPath $targetBundle -Label "Failed new bundle"
            New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
            Assert-AutoGISDirectoryNotReparsePoint -LiteralPath $backupRoot -Label "Backup directory"
            $failedBundleBackup = New-AutoGISUniqueChildPath -Parent $backupRoot -Stem "AutoGIS.Civil3D.Diagnostics.bundle-failed"
            Move-Item -LiteralPath $targetBundle -Destination $failedBundleBackup
        }
        catch {
            $rollbackFailures.Add("Could not preserve the failed new bundle: $($_.Exception.Message)")
        }
    }

    if ($null -ne $previousBundleBackup) {
        if (Test-Path -LiteralPath $targetBundle) {
            $rollbackFailures.Add("Could not restore the previous bundle because the loader target is occupied. The recoverable previous bundle remains at '$previousBundleBackup'.")
        }
        else {
            try {
                Move-Item -LiteralPath $previousBundleBackup -Destination $targetBundle
            }
            catch {
                $rollbackFailures.Add("Could not restore the previous bundle from '$previousBundleBackup': $($_.Exception.Message)")
            }
        }
    }

    if (Test-Path -LiteralPath $stagingBundle) {
        try {
            Assert-AutoGISDirectoryTreeHasNoReparsePoints -LiteralPath $stagingBundle -Label "Failed staging bundle"
            Remove-Item -LiteralPath $stagingBundle -Recurse -Force
        }
        catch {
            $rollbackFailures.Add("Could not remove the failed staging directory '$stagingBundle': $($_.Exception.Message)")
        }
    }

    if ($rollbackFailures.Count -gt 0) {
        $details = $rollbackFailures -join [Environment]::NewLine
        throw "Installation failed: $($installFailure.Exception.Message)`nRollback was incomplete:`n$details"
    }
    throw $installFailure
}

if ($null -ne $previousBundleBackup) {
    Write-Host "Previous bundle moved to: $previousBundleBackup"
}
Write-Host "Installed current-user bundle: $targetBundle"
Write-Host "DLL SHA256: $sourceDllHash"
Write-Host "Restart Civil 3D (or reload bundles with APPAUTOLOADER), then run AUTOGISDIAGNOSTICS."
