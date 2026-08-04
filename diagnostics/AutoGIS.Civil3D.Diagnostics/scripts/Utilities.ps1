function Get-AutoGISFileSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Cannot hash missing file: $LiteralPath"
    }

    $resolvedPath = (Resolve-Path -LiteralPath $LiteralPath).ProviderPath
    $stream = $null
    $algorithm = $null
    try {
        $stream = [System.IO.File]::Open(
            $resolvedPath,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::Read
        )
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        $bytes = $algorithm.ComputeHash($stream)
        return ([System.BitConverter]::ToString($bytes) -replace "-", "")
    }
    finally {
        if ($null -ne $algorithm) {
            $algorithm.Dispose()
        }
        if ($null -ne $stream) {
            $stream.Dispose()
        }
    }
}

function New-AutoGISUniqueChildPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Parent,

        [Parameter(Mandatory = $true)]
        [string]$Stem
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss-fff"
    do {
        $suffix = [Guid]::NewGuid().ToString("N").Substring(0, 8)
        $candidate = Join-Path $Parent "$Stem-$timestamp-$suffix"
    } while (Test-Path -LiteralPath $candidate)

    return $candidate
}

function Assert-AutoGISDirectoryNotReparsePoint {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Container)) {
        throw "$Label is not a directory: $LiteralPath"
    }

    $item = Get-Item -LiteralPath $LiteralPath -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label must not be a symbolic link or junction: $LiteralPath"
    }
}

function Assert-AutoGISDirectoryTreeHasNoReparsePoints {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    Assert-AutoGISDirectoryNotReparsePoint -LiteralPath $LiteralPath -Label $Label

    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push((Resolve-Path -LiteralPath $LiteralPath).ProviderPath)
    while ($pending.Count -gt 0) {
        $current = $pending.Pop()
        foreach ($child in Get-ChildItem -LiteralPath $current -Force) {
            if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "$Label contains a symbolic link or junction: $($child.FullName)"
            }
            if ($child.PSIsContainer) {
                $pending.Push($child.FullName)
            }
        }
    }
}
