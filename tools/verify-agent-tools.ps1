# Read-only availability preflight for optional agent tools.
# Prints one line per tool; never fails, never writes. Policy and fallbacks:
# docs/agent-tools.md.
$ErrorActionPreference = 'SilentlyContinue'

function Report($name, $ok, $detail) {
    $state = if ($ok) { 'available' } else { 'ABSENT  ' }
    Write-Output ("{0}  {1}  {2}" -f $state, $name.PadRight(22), $detail)
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
Report 'gh' ($null -ne $gh) $(if ($gh) { (gh --version | Select-Object -First 1) } else { 'fallback: GitHub web UI; PR/issue state unavailable to scripts' })

$cbm = Get-Command codebase-memory-mcp -ErrorAction SilentlyContinue
if ($cbm) {
    $log = Join-Path $env:USERPROFILE '.cache/codebase-memory-mcp/last-index.log'
    # The log is shared with every repository's hook (AutoGIS writes here too),
    # so the last line may be another repository's. Take this repository's.
    $mine = if (Test-Path $log) { Select-String -Path $log -SimpleMatch ' AutoGIS-Civil3D ' | Select-Object -Last 1 }
    $last = if ($mine) { $mine.Line } else { 'no AutoGIS-Civil3D index entry in the log yet' }
    Report 'codebase-memory-mcp' $true $last
} else {
    Report 'codebase-memory-mcp' $false 'fallback: Grep/Glob or a search subagent'
}

$mnemo = -not [string]::IsNullOrEmpty($env:MNEMOVERSE_API_KEY)
Report 'mnemoverse' $mnemo $(if ($mnemo) { 'API key present in session environment' } else { 'fallback: GitHub issues and PR comments carry cross-session context' })

$py = Get-Command python -ErrorAction SilentlyContinue
Report 'python' ($null -ne $py) $(if ($py) { (python --version) } else { 'REQUIRED for coordination tooling - install before writing' })

exit 0
