# Thin Windows launcher for session-start.sh.
#
# Codex hook environments on Windows resolve git but not bare bash (Git Bash
# is installed but not on PATH), so the POSIX command form fails before the
# script starts. This locates Git Bash relative to git itself — no committed
# absolute path — and hands off to the single canonical script. All logic
# stays in session-start.sh; this file only finds bash.
#
# Never blocks a session: every failure path exits 0.
$ErrorActionPreference = 'SilentlyContinue'

$root = git rev-parse --show-toplevel 2>$null
if (-not $root) { exit 0 }

# git --exec-path -> <git-install>/mingw64/libexec/git-core; bash lives at
# <git-install>/bin/bash.exe (preferred wrapper) or usr/bin/bash.exe.
$execPath = git --exec-path 2>$null
if (-not $execPath) { exit 0 }
$gitInstall = [IO.Path]::GetFullPath((Join-Path $execPath '..\..\..'))
$bash = Join-Path $gitInstall 'bin\bash.exe'
if (-not (Test-Path $bash)) { $bash = Join-Path $gitInstall 'usr\bin\bash.exe' }
if (-not (Test-Path $bash)) { exit 0 }

& $bash "$root/tools/agent-hooks/session-start.sh"
exit 0
