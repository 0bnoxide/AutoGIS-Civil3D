#!/bin/bash
# Shared SessionStart hook for both harnesses. Claude and Codex point here.
#
# One copy on a neutral path is deliberate. AutoGIS keeps a per-harness copy of
# this logic and they have drifted: its .codex copy still announces itself as
# the Claude hook. Neutral paths also keep either harness from reading as the
# architecturally primary one.
#
# Refreshes the codebase-memory knowledge graph and ALWAYS logs the outcome.
# The silent no-op is the failure mode worth designing against: AutoGIS sent
# this output to /dev/null, so when the binary moved the index rotted for weeks
# with no signal. A failed or stale index must be one `cat` away from diagnosis.
#
# Never blocks a session. No `set -e`; every fallible step is guarded and the
# script always exits 0.

set -uo pipefail

log_file="${HOME:-${USERPROFILE:-/tmp}}/.cache/codebase-memory-mcp/last-index.log"
mkdir -p "$(dirname "$log_file")" 2>/dev/null || true

note() {
  echo "$(date -Iseconds 2>/dev/null) AutoGIS-Civil3D $*" >> "$log_file" 2>/dev/null || true
}

# Resolve the canonical repository root rather than the current worktree.
# --git-common-dir resolves a linked worktree back to the primary tree, so a
# worktree session refreshes the one registered project instead of registering
# a separate project per worktree. AutoGIS hardcodes an absolute path here;
# deriving it keeps the hook correct in any checkout, including CI and clones.
common_dir="$(git rev-parse --git-common-dir 2>/dev/null || true)"
if [ -z "$common_dir" ]; then
  note "SKIPPED not a git repository"
  exit 0
fi

# pwd -W yields the Windows form (C:/Users/...). The MCP argument requires that
# form, not the MSYS /c/Users/... form. Fall back where -W is unsupported.
repo_root="$(cd "$(dirname "$common_dir")" 2>/dev/null && { pwd -W 2>/dev/null || pwd; })"
if [ -z "$repo_root" ]; then
  note "SKIPPED could not resolve repository root from $common_dir"
  exit 0
fi

cbm="$(command -v codebase-memory-mcp || true)"
if [ -z "$cbm" ]; then
  note "FAILED codebase-memory-mcp not found on PATH"
  exit 0
fi

if "$cbm" cli index_repository "{\"repo_path\":\"$repo_root\",\"mode\":\"full\"}" >/dev/null 2>&1; then
  # Derived the way the server derives a project name from its path:
  # C:/Users/ichbi/AutoGIS-Civil3D -> C-Users-ichbi-AutoGIS-Civil3D
  project="$(printf '%s' "$repo_root" | sed 's/://g; s#[/\\]#-#g')"
  status="$("$cbm" cli index_status "{\"project\":\"$project\"}" 2>/dev/null || true)"
  note "ok ${status:-status unavailable for $project}"
else
  rc=$?
  note "FAILED index_repository rc=$rc root=$repo_root"
fi

exit 0
