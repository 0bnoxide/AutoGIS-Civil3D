# Claim Session-Pid Tracing — Design

**Status:** Approved 2026-08-08 (owner). Amends PR #65 / issue #36; resolves the
cold review's P1 (transient claim pid) and P2 (no real-CLI claim-path test).

## Problem

Claim records stamp `os.getpid()` of the transient `coordination.py` process
(claim CLI or hook), which exits seconds after claiming. The doctor orphan
finding shipped in PR #65 probes that pid, so virtually every same-host claim
reports as a dead-pid orphan — including claims of live sessions — and doctor
recommends `release --force` against them. The lock-file half of PR #65 is
unaffected: the lock's pid genuinely is the writing process.

## Design

At claim time, record the pid of the nearest long-lived ancestor — the harness
app process (Claude Code / Codex CLI / a human's terminal) — instead of the
CLI's own pid. Doctor, check, release, and the lock path are unchanged.

### `_session_pid() -> int | None`

New helper beside the PR #65 probe helpers in
`tools/agent-coordination/coordination.py`, in two parts:

- **Process table** (platform-specific): one snapshot mapping
  `pid -> (ppid, exe_basename_lower)`. Windows: `ctypes`
  `CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS)` + `Process32FirstW/NextW`
  (stdlib-only, no subprocess). POSIX: one `ps -Ao pid=,ppid=,comm=` via
  `subprocess.run` (Linux and macOS compatible).
- **Pure walk** (unit-testable against synthetic tables): from `os.getpid()`,
  follow ppid links with a visited-set cycle guard (pid reuse can make ppid
  chains loop). Skip ancestors whose executable basename (lowercased,
  `.exe` stripped) starts with `python` or `py`, or equals one of:
  `bash`, `sh`, `dash`, `zsh`, `fish`, `cmd`, `pwsh`, `powershell`,
  `conhost`. Return the first non-skipped ancestor's pid. Return `None`
  when the chain exhausts, the start pid is absent from the table, or a
  cycle is hit.

Failure contract mirrors `_pid_alive`: `_session_pid` never raises; any
failure (snapshot, parse, walk) yields `None`. A `None` pid is stored as JSON
`null`; `_pid_alive(None, ...)` already returns unknown, so doctor degrades
to age-only reporting for that claim — failure produces *less* orphan
detection, never false orphans.

### Record sites

Both claim-record constructors switch `"pid": os.getpid()` to
`"pid": _session_pid()`: the ordinary claim path and `_allocate_adr`.
Registry schema is unchanged (`pid` was already pid-or-whatever-int; it may
now be `null`).

## Constraints

Inherited from the PR #65 spec: stdlib only; never `os.kill` on Windows;
doctor advisory (nothing raises out of `cmd_doctor`); claims never reaped
automatically; ADR claims exempt from orphan findings; `unittest` in temp
repos only.

## Testing

1. **Synthetic-table units** for the pure walk: transient chain resolves to
   the app ancestor; all-transient chain → `None`; start pid missing →
   `None`; cyclic table → `None`.
2. **Live unit**: `_session_pid()` on the test host returns a pid that is
   not `os.getpid()` and is alive per `_pid_alive(_pid_snapshot())`.
3. **End-to-end (P2)**: in a temp repo, claim via the real CLI
   (`subprocess` → `coordination.py claim`), then run doctor in-process:
   the claim must not be reported orphaned. Deterministic both ways — the
   walk either finds a live harness ancestor (alive → no finding) or
   returns `None` (unknown → age-only, no finding). This is the test shape
   that would have caught the P1.

## Rollout

Claims already in the registry keep their recorded transient pids and will
correctly continue to read as orphaned until the owner's one-time
force-release/re-claim at closeout. The amended head of PR #65 gets a fresh
full-tier review.

## Known ceilings

- Name-heuristic skip list: an unlisted shell records the shell (transient →
  false orphan for that setup). The list is a literal tuple; extend on
  contact.
- Pid reuse can still mask a true orphan (inherited from PR #65; the 24h
  age check backstops).
- A human claiming from a terminal records the terminal process, which may
  outlive the working session — fail-safe direction (fewer orphan reports).
