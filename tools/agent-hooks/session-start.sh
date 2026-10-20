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
  return 0
}

# Resolve the canonical repository root rather than the current worktree.
# --git-common-dir resolves a linked worktree back to the primary tree, so a
# worktree session refreshes the one registered project instead of registering
# a separate project per worktree. AutoGIS hardcodes an absolute path here;
# deriving it keeps the hook correct in any checkout, including CI and clones.
#
# Known trade-off, accepted deliberately: the index reflects the primary
# checkout (main), not the session's worktree branch, so graph navigation is
# blind to uncommitted branch work. The MCP derives project identity from
# repo_path and offers no identity override, so indexing the worktree path
# would register a throwaway project per worktree — the pollution visible in
# AutoGIS's project list. Branches here are short-lived; main is the shared
# structure worth navigating. Revisit if the MCP grows a project-name option.
common_dir="$(git rev-parse --git-common-dir 2>/dev/null || true)"
if [[ -z "$common_dir" ]]; then
  note "SKIPPED not a git repository"
  exit 0
fi

# Fast-forward the primary checkout before indexing, so the index follows
# merged work instead of whatever local main last pulled (AutoGIS #588 did the
# same). Through sync-main, the one sanctioned local-main mutation: clean main
# only, ff-only. The outcome rides on the index log line.
primary="$(dirname "$common_dir")"
py="$(command -v python || command -v python3 || true)"
pull="skipped"
if [[ -n "$py" ]]; then
  # Normalize first: on Windows the hook path can arrive with backslashes.
  hook_dir="$(dirname "${BASH_SOURCE[0]//\\//}")"
  GIT_TERMINAL_PROMPT=0 "$py" "$hook_dir/../agent-coordination/coordination.py" sync-main >/dev/null 2>&1
  case $? in
    0) pull="ok" ;;
    1) pull="refused" ;;   # dirty tracked files, or not a fast-forward
    2) pull="skipped" ;;   # primary checkout is not on main
    *) pull="failed" ;;
  esac
fi

# pwd -W yields the Windows form (C:/Users/...). The MCP argument requires that
# form, not the MSYS /c/Users/... form. Fall back where -W is unsupported.
repo_root="$(cd "$(dirname "$common_dir")" 2>/dev/null && { pwd -W 2>/dev/null || pwd; })"
if [[ -z "$repo_root" ]]; then
  note "SKIPPED could not resolve repository root from $common_dir"
  exit 0
fi

cbm="$(command -v codebase-memory-mcp || true)"
if [[ -z "$cbm" ]]; then
  note "FAILED codebase-memory-mcp not found on PATH"
  exit 0
fi

# Derived the way the server derives a project name from its path:
# C:/Users/ichbi/AutoGIS-Civil3D -> C-Users-ichbi-AutoGIS-Civil3D
project="$(printf '%s' "$repo_root" | sed 's/://g; s#[/\\]#-#g; s/^-//')"

# Tell the agent which project to query. The server is one user-scope
# instance shared with every repository on this machine: query tools require
# an explicit `project`, and an agent left to choose from list_projects can
# pick AutoGIS (whose name is a prefix of ours) or a stale per-worktree
# project. SessionStart stdout is added to the agent's context.
echo "codebase-memory: pass project=\"$project\" on every project-scoped codebase-memory call (search, trace, snippet, architecture, index_status) in this repository; list_projects and index_repository take no project. Results from any other project are not this repository's code."

# `ok` alone proved nothing: a poisoned hash cache left 149 File nodes against
# 209+ tracked files and every session still logged ok (#218). So compare the
# graph's File nodes with `git ls-files`, counting only tracked files the
# indexer evidently handles (an extension and top-level directory it has
# indexed elsewhere), which keeps skipped dirs and non-code files from reading
# as stale. Above 5% missing is logged STALE with the fix.
missing_files() {
  local indexed
  indexed="$("$cbm" cli query_graph "{\"project\":\"$project\",\"query\":\"MATCH (f:File) RETURN f.file_path\",\"max_rows\":100000}" 2>/dev/null \
    | sed 's/.*"rows":[[:space:]]*\[//' | grep -o '\["[^"]*"\]' | sed 's/^\["//; s/"\]$//')"
  [[ -n "$indexed" ]] || return 1
  git -C "$primary" ls-files 2>/dev/null | awk '
    function key(p) {
      return ((p ~ /\//) ? substr(p, 1, index(p, "/")) : ".") " " \
             ((p ~ /\.[^.\/]+$/) ? substr(p, match(p, /\.[^.\/]+$/)) : "")
    }
    NR == FNR { have[$0]; seen[key($0)]; next }
    (key($0) in seen) { total++; if (!($0 in have)) { miss++; if (miss <= 3) eg = eg " " $0 } }
    END { printf "%d %d%s\n", miss, total, eg }' <(printf '%s\n' "$indexed") -
}

if "$cbm" cli index_repository "{\"repo_path\":\"$repo_root\",\"mode\":\"full\"}" >/dev/null 2>&1; then
  status="$("$cbm" cli index_status "{\"project\":\"$project\"}" 2>/dev/null || true)"
  status="${status:-status unavailable for $project} pull=$pull"
  if read -r miss total examples < <(missing_files); then
    if (( miss * 20 > total )); then
      note "STALE $miss of $total indexable tracked files missing from the graph (e.g. $examples); fix: delete_project {\"project\":\"$project\"} then index_repository. $status"
    else
      note "ok files=$((total - miss))/$total $status"
    fi
  else
    note "ok files=unchecked $status"
  fi
else
  rc=$?
  note "FAILED index_repository rc=$rc root=$repo_root pull=$pull"
fi

exit 0
