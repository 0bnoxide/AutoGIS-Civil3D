[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$utilitiesPath = Join-Path $PSScriptRoot "Utilities.ps1"
if (-not (Test-Path -LiteralPath $utilitiesPath -PathType Leaf)) {
    throw "The shared script utilities are missing: $utilitiesPath"
}
. $utilitiesPath

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
$targetBundle = Join-Path $appDataRoot "Autodesk\ApplicationPlugins\AutoGIS.Civil3D.Diagnostics.bundle"
if (-not (Test-Path -LiteralPath $targetBundle)) {
    Write-Host "The current-user diagnostic bundle is not installed."
    exit 0
}
Assert-AutoGISDirectoryTreeHasNoReparsePoints -LiteralPath $targetBundle -Label "Installed bundle"

$backupRoot = Join-Path $localAppDataRoot "AutoGIS\PluginBackups"
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
Assert-AutoGISDirectoryNotReparsePoint -LiteralPath $backupRoot -Label "Backup directory"
$backup = New-AutoGISUniqueChildPath -Parent $backupRoot -Stem "AutoGIS.Civil3D.Diagnostics.bundle-uninstalled"

Move-Item -LiteralPath $targetBundle -Destination $backup
Write-Host "Bundle removed from the Autodesk loader path and preserved at: $backup"
