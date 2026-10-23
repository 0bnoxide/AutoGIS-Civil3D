# Final inventory guard for the New Proposal preview package (#235).
# Every direct entry counts, hidden ones included: anything that is not one
# of the exact expected files, or is a directory or reparse point, fails.
# Nothing is deleted to make the check pass.

$PreviewInventory = @(
    "AutoGIS.Civil3D.Adapter.dll",
    "AutoGIS.Civil3D.Proposal.dll",
    "build-info.json",
    "new-proposal-preview.md",
    "new-proposal-smoke.scr"
)

function Assert-PreviewInventory([string]$Directory) {
    $entries = @(Get-ChildItem -LiteralPath $Directory -Force)
    $unexpected = @($entries | Where-Object {
        $_.PSIsContainer -or
        ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or
        $_.Name -cnotin $PreviewInventory
    } | ForEach-Object Name)
    $names = @($entries | ForEach-Object Name | Sort-Object)
    if ($unexpected.Count -ne 0 -or
        (Compare-Object $PreviewInventory $names -CaseSensitive).Count -ne 0) {
        $detail = if ($unexpected.Count -ne 0) { " Unexpected: $($unexpected -join ', ')." } else { "" }
        throw "Preview artifact inventory is not the expected product-only set.$detail"
    }
    return $names
}
