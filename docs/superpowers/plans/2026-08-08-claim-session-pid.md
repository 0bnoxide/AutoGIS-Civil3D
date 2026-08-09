# Claim Session-Pid Tracing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Claims record the pid of the nearest long-lived ancestor (the harness app process) instead of the transient `coordination.py` CLI pid, so doctor's orphan finding (PR #65) stops flagging live sessions' claims — per the approved spec `docs/superpowers/specs/2026-08-08-claim-session-pid-design.md`, resolving the cold review's P1 and P2 on PR #65.

**Architecture:** One process-table snapshot (`pid -> (ppid, exe_basename)`; Windows via ctypes Toolhelp32Snapshot, POSIX via one `ps` call) feeds a pure, unit-testable ancestry walk that skips transient interpreter/shell names and returns the first app ancestor. `_session_pid()` wraps both and never raises; any failure records `null`, which the existing `_pid_alive` probe already treats as unknown → age-only reporting. Only the two claim-record constructors change; doctor, check, release, and the lock path are untouched.

**Tech Stack:** Python stdlib only (`ctypes`, `subprocess`, `os`). Tests: stdlib `unittest` in `tools/agent-coordination/tests/test_coordination.py`, temp repos only.

## Global Constraints

- Repo: `AutoGIS-Civil3D`, worktree `.claude/worktrees/next-task-db706b`, branch `claude/subagent-doctor-orphan-liveness-a16c9c` (amends open PR #65). All paths relative to the worktree root.
- Claims for `tools/agent-coordination/*` and both docs files are held by session `9482fd50-a03b-4465-a85a-9bcb7117be27` — do not re-claim, do not widen.
- Stdlib only; no new dependencies. Tests are `unittest`, never target the real worktree, temp repos only.
- **Never call `os.kill` on Windows** (`os.name == "nt"`).
- `doctor` is advisory and `claim` must keep working when the snapshot fails: `_session_pid()` must never raise; every failure returns `None` (stored as JSON `null` → probes unknown → age-only reporting).
- The lock file keeps `os.getpid()` — the lock's holder genuinely is the writing process. Do not touch `_Lock` or `_lock_finding`.
- Claims are never reaped automatically; ADR claims stay exempt from orphan findings. No doctor changes in this plan.
- Test suite command (from worktree root): `python -m unittest discover -s tools/agent-coordination/tests -v` (append `-k <pattern>` to filter).
- Commit messages: conventional style (`feat:`/`fix:`/`docs:`), reference `(#36)`, end with trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Session-pid helpers

**Files:**
- Modify: `tools/agent-coordination/coordination.py` (new helpers immediately above `def list_claims(repo):`, i.e. directly after `_lock_finding`)
- Test: `tools/agent-coordination/tests/test_coordination.py` (new class at end of file, before the `if __name__ == "__main__":` block)

**Interfaces:**
- Consumes: existing module imports only (`os`, `subprocess` already imported; `ctypes` is imported lazily inside the Windows table builder).
- Produces: `_process_table() -> dict[int, tuple[int, str]] | None` (pid → (ppid, exe basename lowercased, `.exe` stripped); `None` on failure), `_first_app_ancestor(pid, table) -> int | None` (pure walk), `_session_pid() -> int | None` (never raises). Task 2 calls `_session_pid` under exactly this name.

- [ ] **Step 1: Write the failing tests**

Add at the end of `tools/agent-coordination/tests/test_coordination.py`, after `TestDoctorLiveness`, before any `if __name__` block:

```python
class TestSessionPid(unittest.TestCase):
    def test_walk_skips_transients_to_app_ancestor(self):
        table = {10: (20, "python"), 20: (30, "bash"),
                 30: (40, "node"), 40: (0, "explorer")}
        self.assertEqual(coordination._first_app_ancestor(10, table), 30)

    def test_walk_all_transient_chain_returns_none(self):
        table = {10: (20, "pythonw"), 20: (0, "pwsh")}
        self.assertIsNone(coordination._first_app_ancestor(10, table))

    def test_walk_start_pid_missing_returns_none(self):
        self.assertIsNone(
            coordination._first_app_ancestor(99, {1: (0, "node")}))

    def test_walk_cyclic_table_returns_none(self):
        table = {10: (20, "python"), 20: (10, "cmd")}
        self.assertIsNone(coordination._first_app_ancestor(10, table))

    def test_session_pid_live_resolves_alive_non_cli_pid(self):
        pid = coordination._session_pid()
        if pid is None:
            self.skipTest("no resolvable app ancestor on this host")
        self.assertNotEqual(pid, os.getpid())
        self.assertIs(
            coordination._pid_alive(pid, coordination._pid_snapshot()),
            True)
```

(No new test-file imports are needed: `unittest`, `os`, and `coordination` are already imported.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestSessionPid`
Expected: FAIL/ERROR with `AttributeError: module 'coordination' has no attribute '_first_app_ancestor'`

- [ ] **Step 3: Write the helpers**

In `tools/agent-coordination/coordination.py`, insert immediately after `_lock_finding` (still above `def list_claims(repo):`):

```python
_TRANSIENT_ANCESTORS = ("bash", "sh", "dash", "zsh", "fish", "cmd",
                        "pwsh", "powershell", "conhost")


def _process_table_windows():
    import ctypes
    from ctypes import wintypes

    TH32CS_SNAPPROCESS = 0x00000002

    class PROCESSENTRY32W(ctypes.Structure):
        _fields_ = (
            ("dwSize", wintypes.DWORD),
            ("cntUsage", wintypes.DWORD),
            ("th32ProcessID", wintypes.DWORD),
            ("th32DefaultHeapID", ctypes.c_void_p),
            ("th32ModuleID", wintypes.DWORD),
            ("cntThreads", wintypes.DWORD),
            ("th32ParentProcessID", wintypes.DWORD),
            ("pcPriClassBase", ctypes.c_long),
            ("dwFlags", wintypes.DWORD),
            ("szExeFile", ctypes.c_wchar * 260),
        )

    kernel32 = ctypes.windll.kernel32
    # Without restype, the 64-bit HANDLE comes back truncated to int and
    # the INVALID_HANDLE_VALUE comparison can never match.
    kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel32.Process32FirstW.argtypes = (
        wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W))
    kernel32.Process32NextW.argtypes = (
        wintypes.HANDLE, ctypes.POINTER(PROCESSENTRY32W))
    kernel32.CloseHandle.argtypes = (wintypes.HANDLE,)
    snapshot = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snapshot in (None, ctypes.c_void_p(-1).value):
        return None
    table = {}
    try:
        entry = PROCESSENTRY32W()
        entry.dwSize = ctypes.sizeof(PROCESSENTRY32W)
        ok = kernel32.Process32FirstW(snapshot, ctypes.byref(entry))
        while ok:
            name = entry.szExeFile.lower()
            if name.endswith(".exe"):
                name = name[:-4]
            table[int(entry.th32ProcessID)] = (
                int(entry.th32ParentProcessID), name)
            ok = kernel32.Process32NextW(snapshot, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snapshot)
    return table or None


def _process_table_posix():
    proc = subprocess.run(
        ["ps", "-Ao", "pid=,ppid=,comm="],
        capture_output=True, text=True, encoding="utf-8",
        errors="replace", timeout=30,
    )
    if proc.returncode != 0:
        return None
    table = {}
    for line in proc.stdout.splitlines():
        parts = line.split(None, 2)
        if len(parts) < 3:
            continue
        try:
            pid, ppid = int(parts[0]), int(parts[1])
        except ValueError:
            continue
        table[pid] = (ppid, os.path.basename(parts[2].strip()).lower())
    return table or None


def _process_table():
    """pid -> (ppid, exe basename lowercased, `.exe` stripped) for every
    live process, or None when the snapshot fails."""
    if os.name == "nt":
        return _process_table_windows()
    return _process_table_posix()


def _first_app_ancestor(pid, table):
    """Nearest ancestor of `pid` that is not a transient interpreter or
    shell (python*/py*, bash, cmd, pwsh, ...), or None when the chain is
    all-transient, leaves the table, or cycles (pid reuse can loop a
    ppid chain)."""
    seen = set()
    while pid in table and pid not in seen:
        seen.add(pid)
        ppid, name = table[pid]
        if not (name.startswith("py") or name in _TRANSIENT_ANCESTORS):
            return pid
        pid = ppid
    return None


def _session_pid():
    """Pid of the nearest long-lived ancestor — the harness app process
    (claude/node, codex, or a human's terminal) — or None when unknowable.

    Claims outlive the transient coordination.py process that records
    them; stamping os.getpid() here made doctor's orphan probe flag every
    claim as dead moments later (#36). A None pid is stored as JSON null
    and probes as unknown -> age-only reporting.
    """
    try:
        table = _process_table()
        if not table:
            return None
        return _first_app_ancestor(os.getpid(), table)
    except Exception:
        # Deliberately broad: claim must keep working on any platform
        # quirk, degrading to age-only reporting, never a wrong pid.
        return None
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestSessionPid`
Expected: 4 PASS + the live test PASS (or SKIP on a host with no resolvable app ancestor — on this Windows workstation it should PASS).

- [ ] **Step 5: Commit**

```bash
git add tools/agent-coordination/coordination.py tools/agent-coordination/tests/test_coordination.py
git commit -m "feat: session-pid tracing helpers for claim records (#36)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Record session pid at both claim sites, end-to-end CLI test

**Files:**
- Modify: `tools/agent-coordination/coordination.py` (the two claim-record constructors: the `record = {...}` dict inside the ordinary claim path and the one inside `_allocate_adr` — both currently contain `"pid": os.getpid(),`)
- Modify: `tools/agent-coordination/tests/test_coordination.py` (one test added to `TestDoctorLiveness`)

**Interfaces:**
- Consumes: `_session_pid()` from Task 1, exactly as defined there. Existing: `TestDoctorLiveness` fixture (`self.repo_path`, `self.repo`, `doctor_output()`), `coordination.list_claims(repo)`, `sys`/`subprocess` (already imported in the test file).
- Produces: claim records whose `pid` field is the session pid or `null`. No signature changes anywhere.

- [ ] **Step 1: Write the failing test**

Add to `TestDoctorLiveness` (after `test_probe_unknown_falls_back_to_age_only`):

```python
    def test_cli_claim_not_reported_orphaned(self):
        proc = subprocess.run(
            [sys.executable, coordination.__file__, "claim",
             "--session", "cli-sess", "--kind", "branch",
             "--value", "feature-cli"],
            capture_output=True, text=True, cwd=self.repo_path, timeout=60)
        self.assertEqual(proc.returncode, 0, proc.stderr)
        self.assertEqual(len(coordination.list_claims(self.repo)), 1)
        self.assertNotIn("orphaned claim", self.doctor_output())
```

(This is the review-P2 test: it claims through the real CLI — a subprocess that has exited by the time doctor runs — instead of injecting a pid by hand. The CLI self-initializes the registry in a fresh temp repo: `_Lock.__enter__` creates the state dir and `_load` returns an empty registry when the file is missing, so no `init` call is needed.)

- [ ] **Step 2: Run test to verify it fails**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k test_cli_claim_not_reported_orphaned`
Expected: FAIL — doctor output contains `orphaned claim` because the record still carries the dead CLI pid. (This deterministically reproduces the P1.)

- [ ] **Step 3: Switch both record sites**

In `tools/agent-coordination/coordination.py`, in the ordinary claim path's `record = {...}` constructor, replace:

```python
        "pid": os.getpid(),
```

with:

```python
        "pid": _session_pid(),
```

Then make the identical one-line replacement in `_allocate_adr`'s `record = {...}` constructor. (Two occurrences total; the module has no other `"pid":` record constructors. Do not touch the lock-file write in `_Lock.__enter__` — that `os.getpid()` is correct.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestDoctorLiveness`
Expected: all `TestDoctorLiveness` tests PASS, including the new CLI test (after the fix the claim records a live app-ancestor pid, or `null` → unknown → age-only; either way no orphan finding).

- [ ] **Step 5: Run the FULL suite**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v`
Expected: everything passes. The pre-existing `TestDoctorLiveness` tests inject pids explicitly via `write_claim(pid=...)`, so they are unaffected by the record-site change.

- [ ] **Step 6: Commit**

```bash
git add tools/agent-coordination/coordination.py tools/agent-coordination/tests/test_coordination.py
git commit -m "fix(claim): record session pid, not the transient CLI pid (#36)

Resolves PR #65 review P1 (every claim read as orphaned) and P2
(no real-CLI claim-path test).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Ship the amendment (controller/owner-gated)

**Files:** none (push + PR bookkeeping only).

**Interfaces:**
- Consumes: everything above, plus the spec commit already on the branch.
- Produces: updated PR #65 head + a PR comment resolving the cold review's P1/P2; fresh full-tier review requested.

- [ ] **Step 1: Push**

```bash
git push origin claude/subagent-doctor-orphan-liveness-a16c9c
```

- [ ] **Step 2: Comment on PR #65**

Post a comment stating: P1 resolved by recording `_session_pid()` (nearest non-transient ancestor; `null` → age-only fallback) at both claim-record sites; P2 resolved by `test_cli_claim_not_reported_orphaned`, which claims through the real CLI subprocess and asserts doctor stays quiet; spec at `docs/superpowers/specs/2026-08-08-claim-session-pid-design.md`; name the new head sha. Note the rollout caveat: claims already in the registry keep their old transient pids and will correctly read as orphaned until the owner's one-time force-release/re-claim at closeout.

- [ ] **Step 3: Request a fresh full-tier review of the new head** (per the cold reviewer's rule that any fix produces a new head requiring re-review). Do NOT merge — merge sign-off is the owner's.
