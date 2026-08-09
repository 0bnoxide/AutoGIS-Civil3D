# Doctor Dead-Pid Orphan Liveness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `coordination.py doctor` report same-host dead-pid orphaned claims immediately (any age) and name the registry-lock holder's liveness, per issue #36 and the approved spec `docs/superpowers/specs/2026-08-07-doctor-orphan-liveness-design.md`.

**Architecture:** One batched `tasklist /FO CSV /NH` snapshot per doctor run on Windows (a `set` of live pids); `os.kill(pid, 0)` per pid on POSIX. A tri-state probe (`True`/`False`/`None`-unknown) feeds two consumers inside `cmd_doctor`: the claims loop (new "orphaned claim" finding) and the registry-lock finding (holder dead / alive / neutral). Everything degrades to today's age-only behavior on probe failure. No enforcement or release changes.

**Tech Stack:** Python stdlib only (`csv`, `subprocess`, `os`, `socket`, `re`). Tests: stdlib `unittest` in `tools/agent-coordination/tests/test_coordination.py`, temp repos only.

## Global Constraints

- Repo: `AutoGIS-Civil3D`, worktree `.worktrees/claude+doctor-orphan-liveness`, branch `claude/doctor-orphan-liveness`. All paths below are relative to the worktree root.
- Claims for `tools/agent-coordination/*` and both docs files are already held by session `claude-issue36-011d47a4` — do not re-claim, do not widen.
- Stdlib only; no new dependencies. Tests are `unittest`, never target the real worktree, temp repos only.
- **Never call `os.kill` on Windows** (`os.name == "nt"`) — it maps to `TerminateProcess` and would kill a live process. The probe answers only from the tasklist snapshot there.
- `doctor` is advisory: the probe must never raise out of `cmd_doctor` and probe failure must never abort the run — unknown liveness falls back to the existing age-only reporting.
- ADR-kind claims stay exempt from both stale-suspect and orphan findings (they are meant to outlive the claiming process).
- Claims are never reaped automatically — findings only; `release --force` remains the sole clearing path.
- Test suite command (from worktree root): `python -m unittest discover -s tools/agent-coordination/tests -v` (append `-k <pattern>` to filter).
- Commit messages: conventional style (`feat:`/`test:`/`docs:`), reference `(#36)`, and end with the trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Liveness probe helpers

**Files:**
- Modify: `tools/agent-coordination/coordination.py` (imports block ~line 25; new helpers just above `def list_claims` ~line 1007)
- Test: `tools/agent-coordination/tests/test_coordination.py` (new class at end of file; imports block ~line 8)

**Interfaces:**
- Consumes: nothing new — existing module imports (`os`, `subprocess`, `socket` already imported; `csv` is NOT yet imported, this task adds it).
- Produces: `_pid_snapshot() -> set[int] | None` (Windows: live-pid set from one `tasklist` call, `None` on any failure; POSIX: always `None`) and `_pid_alive(pid, snapshot) -> bool | None` (`True` alive / `False` dead / `None` unknown; accepts any pid-ish value, never raises). Tasks 2 and 3 call both under exactly these names.

- [ ] **Step 1: Write the failing tests**

In `tools/agent-coordination/tests/test_coordination.py`, extend the imports block (top of file) with the two missing modules:

```python
import datetime as _dt
import socket
```

(keep the existing imports; `_dt` and `socket` are used by this and later tasks). Then add at the end of the file, before any `if __name__` block if one exists (otherwise just at the end):

```python
class TestDoctorLiveness(TempRepoCase):
    @staticmethod
    def dead_pid():
        proc = subprocess.Popen([sys.executable, "-c", "pass"])
        proc.wait()
        return proc.pid

    def test_pid_alive_true_for_own_process(self):
        snapshot = coordination._pid_snapshot()
        self.assertIs(coordination._pid_alive(os.getpid(), snapshot), True)

    def test_pid_alive_unknown_for_garbage_pid(self):
        self.assertIsNone(coordination._pid_alive("garbage", None))
        self.assertIsNone(coordination._pid_alive(-4, set()))
        self.assertIsNone(coordination._pid_alive(None, {1, 2}))
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestDoctorLiveness`
Expected: FAIL/ERROR with `AttributeError: module 'coordination' has no attribute '_pid_snapshot'`

- [ ] **Step 3: Write the helpers**

In `tools/agent-coordination/coordination.py`: add `import csv` to the imports block (alphabetical position, after `import argparse` / before `import datetime as _dt`). Then insert immediately above `def list_claims(repo):`:

```python
def _pid_snapshot():
    """Windows: the set of live pids from one `tasklist` call, or None if
    the snapshot could not be taken. POSIX: always None — os.kill probes
    each pid directly, no snapshot needed."""
    if os.name != "nt":
        return None
    try:
        proc = subprocess.run(
            ["tasklist", "/FO", "CSV", "/NH"],
            capture_output=True, text=True, encoding="utf-8",
            errors="replace", timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired):
        return None
    if proc.returncode != 0:
        return None
    pids = set()
    for row in csv.reader(proc.stdout.splitlines()):
        if len(row) < 2:
            continue
        try:
            pids.add(int(row[1]))
        except ValueError:
            continue
    # An empty set means the parse found no processes at all — impossible
    # on a live system, so treat it as a failed snapshot, not "all dead".
    return pids or None


def _pid_alive(pid, snapshot):
    """Liveness of a local pid: True, False, or None (unknown).

    Windows answers only from the tasklist snapshot — os.kill(pid, 0)
    maps to TerminateProcess there and would kill a live holder.
    """
    try:
        pid = int(pid)
    except (TypeError, ValueError):
        return None
    if pid <= 0:
        return None
    if os.name == "nt":
        # ponytail: a recycled pid makes a dead holder look alive; the
        # STALE_SUSPECT_HOURS age check still catches it eventually.
        # Upgrade path: OpenProcess + process creation-time comparison.
        return None if snapshot is None else pid in snapshot
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    except OSError:
        return None
    return True
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestDoctorLiveness`
Expected: 2 tests PASS (`dead_pid` is exercised by later tasks).

- [ ] **Step 5: Commit**

```bash
git add tools/agent-coordination/coordination.py tools/agent-coordination/tests/test_coordination.py
git commit -m "feat: tri-state local pid liveness probe for doctor (#36)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Orphaned-claim finding in the doctor claims loop

**Files:**
- Modify: `tools/agent-coordination/coordination.py` (claims loop inside `cmd_doctor`, currently ~lines 1086-1100: the `try: claims = list_claims(repo)` block)
- Modify: `tools/agent-coordination/tests/test_coordination.py` (move `doctor_output` helper; add tests to `TestDoctorLiveness`)

**Interfaces:**
- Consumes: `_pid_snapshot()` and `_pid_alive(pid, snapshot)` from Task 1, exactly as defined there. Existing: `list_claims(repo)`, `STALE_SUSPECT_HOURS`, `socket.gethostname()`, `coordination._empty_registry()`, `coordination._save(path, data)`, `coordination._now()`, `repo.registry_path`, the `TempRepoCase` fixture.
- Produces: a doctor finding formatted exactly as
  `orphaned claim <id> (<kind>=<value>, session <session>) — pid <pid> is dead on this host; release --force --id <id> --reason ... to clear`
  and a `snapshot` local variable inside `cmd_doctor` computed once before the registry-lock check (Task 3 reuses it). Test-side: `doctor_output()` now lives on `TempRepoCase`; `write_claim(**overrides)` helper on `TestDoctorLiveness`.

- [ ] **Step 1: Move `doctor_output` up to the shared fixture (DRY)**

In `test_coordination.py`, cut the `doctor_output` method out of `TestCodexHookTrust` (it is at ~line 61) and paste it unchanged as a method of `TempRepoCase`:

```python
    def doctor_output(self):
        buffer = io.StringIO()
        with redirect_stdout(buffer):
            rc = coordination.cmd_doctor(self.repo)
        self.assertEqual(rc, coordination.ALLOW)
        return buffer.getvalue()
```

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestCodexHookTrust`
Expected: all existing hook-trust tests still PASS.

- [ ] **Step 2: Write the failing tests**

Add to `TestDoctorLiveness`:

```python
    def write_claim(self, **overrides):
        record = {"id": "abc123def456", "session": "sess-1", "harness": "",
                  "pid": os.getpid(), "host": socket.gethostname(),
                  "kind": "branch", "value": "feature-x",
                  "created_utc": coordination._now()}
        record.update(overrides)
        data = coordination._empty_registry()
        data["claims"].append(record)
        os.makedirs(os.path.dirname(self.repo.registry_path), exist_ok=True)
        coordination._save(self.repo.registry_path, data)

    def test_same_host_dead_pid_claim_reported_orphaned(self):
        self.write_claim(pid=self.dead_pid())
        out = self.doctor_output()
        self.assertIn("orphaned claim abc123def456", out)
        self.assertIn("release --force --id abc123def456", out)

    def test_live_claim_not_reported_orphaned(self):
        self.write_claim()  # own pid, own host
        self.assertNotIn("orphaned claim", self.doctor_output())

    def test_foreign_host_dead_pid_not_reported_orphaned(self):
        self.write_claim(pid=self.dead_pid(), host="elsewhere")
        self.assertNotIn("orphaned claim", self.doctor_output())

    def test_adr_claim_exempt_from_orphan_check(self):
        self.write_claim(kind="adr", value="0099", pid=self.dead_pid())
        self.assertNotIn("orphaned claim", self.doctor_output())

    def test_probe_unknown_falls_back_to_age_only(self):
        old = (_dt.datetime.now(_dt.timezone.utc)
               - _dt.timedelta(hours=48)).strftime("%Y-%m-%dT%H:%M:%SZ")
        self.write_claim(pid=self.dead_pid(), created_utc=old)
        with mock.patch.object(coordination, "_pid_alive",
                               return_value=None):
            out = self.doctor_output()
        self.assertNotIn("orphaned claim", out)
        self.assertIn("stale-suspect claim", out)
```

(Fresh-orphan visibility — the whole point of #36 — is proven by the first test: the claim is seconds old, far under `STALE_SUSPECT_HOURS`, yet reported. Known micro-flake ceiling: the OS could recycle `dead_pid()` between `wait()` and the probe; vanishingly rare, accepted.)

- [ ] **Step 3: Run tests to verify they fail**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestDoctorLiveness`
Expected: `test_same_host_dead_pid_claim_reported_orphaned` FAILS (no "orphaned claim" in output); `test_probe_unknown_falls_back_to_age_only` may already pass (stale-suspect logic exists) — that is fine; the orphan assertions are the red ones.

- [ ] **Step 4: Implement the doctor loop change**

In `cmd_doctor`, replace the existing claims block:

```python
    try:
        claims = list_claims(repo)
        now = _dt.datetime.now(_dt.timezone.utc)
        for record in claims:
            created = _dt.datetime.strptime(
                record["created_utc"], "%Y-%m-%dT%H:%M:%SZ"
            ).replace(tzinfo=_dt.timezone.utc)
            age_h = (now - created).total_seconds() / 3600
            if record["kind"] != "adr" and age_h > STALE_SUSPECT_HOURS:
                findings.append(
                    f"stale-suspect claim {record['id']} ({record['kind']}="
                    f"{record['value']}, session {record['session']}, "
                    f"{age_h:.0f}h old) — NOT expired; release explicitly "
                    "if orphaned"
                )
    except RegistryError as exc:
        findings.append(str(exc))
```

with:

```python
    try:
        claims = list_claims(repo)
        now = _dt.datetime.now(_dt.timezone.utc)
        local_host = socket.gethostname()
        for record in claims:
            if record["kind"] == "adr":
                continue  # ADR reservations are meant to outlive their process
            created = _dt.datetime.strptime(
                record["created_utc"], "%Y-%m-%dT%H:%M:%SZ"
            ).replace(tzinfo=_dt.timezone.utc)
            age_h = (now - created).total_seconds() / 3600
            if (record.get("host") == local_host
                    and _pid_alive(record.get("pid"), snapshot) is False):
                findings.append(
                    f"orphaned claim {record['id']} ({record['kind']}="
                    f"{record['value']}, session {record['session']}) — "
                    f"pid {record['pid']} is dead on this host; "
                    f"release --force --id {record['id']} --reason ... "
                    "to clear"
                )
            elif age_h > STALE_SUSPECT_HOURS:
                findings.append(
                    f"stale-suspect claim {record['id']} ({record['kind']}="
                    f"{record['value']}, session {record['session']}, "
                    f"{age_h:.0f}h old) — NOT expired; release explicitly "
                    "if orphaned"
                )
    except RegistryError as exc:
        findings.append(str(exc))
```

`snapshot` is not defined yet inside `cmd_doctor` — add it once, immediately BEFORE the registry-lock check near the top of `cmd_doctor` (the `lock = repo.registry_path + LOCK_SUFFIX` line), so Task 3 can reuse it:

```python
    snapshot = _pid_snapshot()
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestDoctorLiveness`
Expected: all `TestDoctorLiveness` tests PASS.

- [ ] **Step 6: Commit**

```bash
git add tools/agent-coordination/coordination.py tools/agent-coordination/tests/test_coordination.py
git commit -m "feat(doctor): surface same-host dead-pid orphaned claims at any age (#36)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Registry-lock holder liveness

**Files:**
- Modify: `tools/agent-coordination/coordination.py` (new `_lock_finding` helper next to the Task 1 helpers; the lock check inside `cmd_doctor`)
- Modify: `tools/agent-coordination/tests/test_coordination.py` (add tests to `TestDoctorLiveness`)

**Interfaces:**
- Consumes: `_pid_alive(pid, snapshot)` from Task 1; the `snapshot` variable Task 2 placed before the lock check; existing `LOCK_SUFFIX`, `re`, `socket`. Lock-file content format (written by `_Lock.__enter__`): `<pid>@<hostname> <iso-timestamp>`.
- Produces: `_lock_finding(lock_path, snapshot) -> str` returning one of three exact messages: neutral `registry lock present: <path> — verify the holder is alive before removing manually`; dead `registry lock present: <path> — holder pid <pid> is dead on this host; safe to remove`; alive `registry lock present: <path> — holder pid <pid> appears alive; hands off`.

- [ ] **Step 1: Write the failing tests**

Add to `TestDoctorLiveness`:

```python
    def write_lock(self, content):
        lock = self.repo.registry_path + coordination.LOCK_SUFFIX
        os.makedirs(os.path.dirname(lock), exist_ok=True)
        with open(lock, "w", encoding="utf-8") as fh:
            fh.write(content)

    def test_lock_with_dead_local_holder_reported_removable(self):
        pid = self.dead_pid()
        self.write_lock(f"{pid}@{socket.gethostname()} 2026-08-07T00:00:00Z")
        self.assertIn(
            f"holder pid {pid} is dead on this host; safe to remove",
            self.doctor_output())

    def test_lock_with_live_local_holder_reported_alive(self):
        self.write_lock(
            f"{os.getpid()}@{socket.gethostname()} 2026-08-07T00:00:00Z")
        self.assertIn(f"holder pid {os.getpid()} appears alive; hands off",
                      self.doctor_output())

    def test_lock_with_garbage_content_keeps_neutral_wording(self):
        self.write_lock("not a holder line")
        self.assertIn("verify the holder is alive before removing manually",
                      self.doctor_output())

    def test_lock_on_foreign_host_keeps_neutral_wording(self):
        self.write_lock(f"{self.dead_pid()}@elsewhere 2026-08-07T00:00:00Z")
        self.assertIn("verify the holder is alive before removing manually",
                      self.doctor_output())
```

(No lock cleanup needed: the lock lives inside `self.base`, which `TempRepoCase.setUp` already removes via `addCleanup`. `list_claims` reads without acquiring the lock, so a fake lock file cannot deadlock doctor.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k lock_with`
Expected: dead-holder and live-holder tests FAIL (doctor still prints only the neutral wording); the garbage and foreign-host tests already PASS against the old message — the red pair is the target.

- [ ] **Step 3: Implement `_lock_finding` and wire it in**

Insert next to the Task 1 helpers (above `def list_claims(repo):`):

```python
def _lock_finding(lock_path, snapshot):
    """Describe a present registry lock, naming holder liveness when the
    lock records a parseable same-host holder; otherwise stay neutral."""
    neutral = (f"registry lock present: {lock_path} — verify the holder "
               "is alive before removing manually")
    try:
        with open(lock_path, encoding="utf-8") as fh:
            content = fh.read()
    except OSError:
        return neutral
    match = re.match(r"(\d+)@(\S+) ", content)
    if not match or match.group(2) != socket.gethostname():
        return neutral
    alive = _pid_alive(int(match.group(1)), snapshot)
    if alive is False:
        return (f"registry lock present: {lock_path} — holder pid "
                f"{match.group(1)} is dead on this host; safe to remove")
    if alive is True:
        return (f"registry lock present: {lock_path} — holder pid "
                f"{match.group(1)} appears alive; hands off")
    return neutral
```

In `cmd_doctor`, replace the existing lock finding:

```python
    lock = repo.registry_path + LOCK_SUFFIX
    if os.path.exists(lock):
        findings.append(f"registry lock present: {lock} — verify the holder "
                        "is alive before removing manually")
```

with:

```python
    lock = repo.registry_path + LOCK_SUFFIX
    if os.path.exists(lock):
        findings.append(_lock_finding(lock, snapshot))
```

(The `snapshot = _pid_snapshot()` line from Task 2 Step 4 sits immediately above this block — keep it above so both consumers share one snapshot.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v -k TestDoctorLiveness`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add tools/agent-coordination/coordination.py tools/agent-coordination/tests/test_coordination.py
git commit -m "feat(doctor): name registry-lock holder liveness when knowable (#36)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Docstring, full suite, push, PR

**Files:**
- Modify: `tools/agent-coordination/coordination.py` (module docstring, ~lines 15-21)

**Interfaces:**
- Consumes: everything above.
- Produces: the shipped branch + a PR referencing #36.

- [ ] **Step 1: Update the module-docstring invariant**

In the module docstring, replace:

```
- Claims never expire and are never reaped automatically. `doctor` reports
  stale-suspect claims; only an explicit `release --force <id> --reason ...`
  clears an orphan.
```

with:

```
- Claims never expire and are never reaped automatically. `doctor` reports
  stale-suspect claims and same-host dead-pid orphans; only an explicit
  `release --force <id> --reason ...` clears an orphan.
```

- [ ] **Step 2: Run the FULL coordination suite**

Run: `python -m unittest discover -s tools/agent-coordination/tests -v`
Expected: everything passes, including all pre-existing tests. If any pre-existing test asserted the old full lock-finding string, it still passes — the neutral wording is preserved verbatim for unparseable/foreign/unknown cases, and only the suffix after the path changes for known holders; if one fails anyway, fix the assertion to match the new exact message from Task 3's Interfaces block, never by weakening the implementation.

- [ ] **Step 3: Commit the docstring**

```bash
git add tools/agent-coordination/coordination.py
git commit -m "docs: doctor invariant now covers same-host dead-pid orphans (#36)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 4: Push and open the PR**

```bash
git push -u origin claude/doctor-orphan-liveness
gh pr create --repo 0bnoxide/AutoGIS-Civil3D \
  --title "fix(doctor): surface same-host dead-pid orphaned claims immediately (#36)" \
  --body "Closes #36. Implements the approved spec docs/superpowers/specs/2026-08-07-doctor-orphan-liveness-design.md: batched tasklist snapshot on Windows / os.kill(0) on POSIX, tri-state probe, orphan finding at any age for same-host dead-pid claims, lock-holder liveness in the lock finding, fail-open to age-only on probe failure. Deliberate scope: no auto-release (never-reap invariant), no enforcement-path changes, ADR claims exempt. Known ceiling: pid reuse can mask an orphan; the 24h age check still catches it — upgrade path is OpenProcess + creation-time comparison.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```

Do NOT merge — merge sign-off is the owner's. The issue asked for "its own claimed slice and review tier": request review per this repo's normal review flow after the PR opens.

- [ ] **Step 5: Release claims only after merge/closeout** (owner-gated; when instructed):

```bash
python tools/agent-coordination/coordination.py release --id a5631e522617 --session claude-issue36-011d47a4   # branch
python tools/agent-coordination/coordination.py release --id 99862473a12f --session claude-issue36-011d47a4   # worktree
python tools/agent-coordination/coordination.py release --id 271ff41a9a5e --session claude-issue36-011d47a4   # tools/agent-coordination/*
python tools/agent-coordination/coordination.py release --id 950ba3591d69 --session claude-issue36-011d47a4   # spec doc
python tools/agent-coordination/coordination.py release --id e643c3a57a10 --session claude-issue36-011d47a4   # plan doc
git worktree remove .worktrees/claude+doctor-orphan-liveness
```
