# Doctor Session PID-Spread Implementation Plan

**Design:** [Doctor Session PID-Spread Design](../specs/2026-08-16-doctor-pid-spread-design.md)

**Tech Stack:** Python standard library, `unittest`, existing coordination test utilities.

---

### Task 1: Degrade multi-PID sessions to age-only doctor reporting

**Files:**
- Modify: `tools/agent-coordination/tests/test_coordination.py:1160-1265`
- Modify: `tools/agent-coordination/coordination.py:1330-1385`

**Interfaces:**
- Consumes: claim dictionaries already returned by `list_claims(repo)` and the existing `_pid_alive(pid, snapshot) -> bool | None` probe.
- Produces: unchanged `cmd_doctor(repo) -> int` CLI behavior, except multi-PID local sessions use the existing stale-suspect path.

#### Step 1: Add a multi-claim test utility and eight focused behavior tests

Keep `write_claim(**overrides)` as the existing one-record convenience method and add a test-only `write_claims(*overrides)` helper that writes complete claim records. Add tests named:

```python
def write_claims(self, *overrides):
    base = {"id": "abc123def456", "session": "sess-1", "harness": "",
            "pid": os.getpid(), "host": socket.gethostname(),
            "kind": "branch", "value": "feature-x",
            "created_utc": coordination._now()}
    data = coordination._empty_registry()
    data["claims"] = [{**base, **record} for record in overrides]
    os.makedirs(os.path.dirname(self.repo.registry_path), exist_ok=True)
    coordination._save(self.repo.registry_path, data)

def write_claim(self, **overrides):
    self.write_claims(overrides)

def test_distinct_pid_spread_falls_back_to_age_only(self):
    old = (_dt.datetime.now(_dt.timezone.utc)
           - _dt.timedelta(hours=48)).strftime("%Y-%m-%dT%H:%M:%SZ")
    self.write_claims(
        {"id": "spread000001", "pid": 101, "created_utc": old},
        {"id": "spread000002", "pid": "202", "created_utc": old,
         "kind": "worktree", "value": ".worktrees/codex+issue-91"},
    )
    with mock.patch.object(coordination, "_pid_alive", return_value=False):
        out = self.doctor_output()
    self.assertNotIn("orphaned claim spread", out)
    self.assertIn("stale-suspect claim spread000001", out)
    self.assertIn("stale-suspect claim spread000002", out)

def test_pid_spread_rejects_repeated_dead_pid(self):
    self.write_claims(
        {"id": "repeat000001", "pid": 101},
        {"id": "repeat000002", "pid": "101", "kind": "worktree",
         "value": ".worktrees/codex+issue-91"},
    )
    with mock.patch.object(coordination, "_pid_alive", return_value=False):
        out = self.doctor_output()
    self.assertIn("orphaned claim repeat000001", out)
    self.assertIn("orphaned claim repeat000002", out)

def test_pid_spread_is_scoped_to_session(self):
    self.write_claims(
        {"id": "sess10000001", "pid": 101, "session": "sess-1"},
        {"id": "sess20000002", "pid": 202, "session": "sess-2"},
    )
    with mock.patch.object(coordination, "_pid_alive", return_value=False):
        out = self.doctor_output()
    self.assertIn("orphaned claim sess10000001", out)
    self.assertIn("orphaned claim sess20000002", out)

def test_pid_spread_is_scoped_to_host(self):
    self.write_claims(
        {"id": "local0000001", "pid": 101},
        {"id": "foreign00001", "pid": 202, "host": "elsewhere"},
    )
    with mock.patch.object(coordination, "_pid_alive", return_value=False):
        out = self.doctor_output()
    self.assertIn("orphaned claim local0000001", out)
    self.assertNotIn("orphaned claim foreign00001", out)

def test_pid_spread_ignores_unusable_pid(self):
    self.write_claims(
        {"id": "valid0000001", "pid": 101},
        {"id": "zero00000002", "pid": 0, "kind": "worktree",
         "value": ".worktrees/codex+issue-91"},
    )
    with mock.patch.object(coordination, "_pid_alive", return_value=False):
        out = self.doctor_output()
    self.assertIn("orphaned claim valid0000001", out)

def test_pid_spread_ignores_adr_pid(self):
    self.write_claims(
        {"id": "valid0000001", "pid": 101},
        {"id": "adr000000002", "pid": 202, "kind": "adr",
         "value": "0005"},
    )
    with mock.patch.object(coordination, "_pid_alive", return_value=False):
        out = self.doctor_output()
    self.assertIn("orphaned claim valid0000001", out)
    self.assertNotIn("orphaned claim adr000000002", out)

def test_pid_spread_ignores_unusable_session(self):
    self.write_claim(session=["bad"], pid=101)
    with mock.patch.object(coordination, "_pid_alive", return_value=False):
        out = self.doctor_output()
    self.assertIn("orphaned claim abc123def456", out)

def test_pid_spread_ignores_nonfinite_pid(self):
    old = (_dt.datetime.now(_dt.timezone.utc)
           - _dt.timedelta(hours=48)).strftime("%Y-%m-%dT%H:%M:%SZ")
    self.write_claim(pid=float("inf"), created_utc=old)
    out = self.doctor_output()
    self.assertNotIn("orphaned claim abc123def456", out)
    self.assertIn("stale-suspect claim abc123def456", out)
```

The helper builds the existing complete base record, merges each override,
stores all records in one `_empty_registry()`, and calls `_save()` once. Tests
assert only real `doctor` output; the mock controls the external process-table
boundary and receives no assertions.

#### Step 2: Run the focused tests and verify RED

Run at host scope:

```powershell
python -m unittest discover -s tools/agent-coordination/tests -p test_coordination.py -k pid_spread
```

Expected on the issue baseline: the fallback test fails because current
`cmd_doctor` emits orphan findings, the non-finite-PID test errors because
`_pid_alive()` does not catch `OverflowError`, and the remaining six tests
pass. During implementation, run the malformed tests again whenever
normalization or membership logic changes so regressions fail directly as
uncaught test errors.

#### Step 3: Implement the minimum session-spread guard in `cmd_doctor`

After `claims`, `now`, and `local_host` are available, derive unreliable sessions once:

```python
session_pids = {}
for record in claims:
    if (not isinstance(record, dict)
            or record.get("host") != local_host
            or record.get("kind") in (None, "adr")):
        continue
    session = record.get("session")
    if not isinstance(session, str) or not session:
        continue
    try:
        pid = int(record.get("pid"))
    except (TypeError, ValueError, OverflowError):
        continue
    if pid > 0:
        session_pids.setdefault(session, set()).add(pid)
unreliable_pid_sessions = {
    session for session, pids in session_pids.items() if len(pids) > 1
}
```

Add one condition to the existing orphan branch:

```python
if (record.get("host") == local_host
        and (not isinstance(record.get("session"), str)
             or record.get("session") not in unreliable_pid_sessions)
        and _pid_alive(record.get("pid"), snapshot) is False):
```

Add `OverflowError` to `_pid_alive`'s matching conversion guard as well. Do not add a helper or change messages; falling through to the existing age branch is the feature.

#### Step 4: Run focused tests and verify GREEN

Run at host scope:

```powershell
python -m unittest discover -s tools/agent-coordination/tests -p test_coordination.py -k pid_spread
```

Expected: all eight PID-spread tests pass.

#### Step 5: Run the complete repository verification

Run at host scope where noted:

```powershell
python -m unittest discover -s tools/agent-coordination/tests
python tools/agent-assets/sync.py --check
python -m unittest tools/agent-assets/tests/test_sync.py
dotnet test -c Release
python tools/checks/docs_checks.py
```

Expected: every command exits `0`. Then scan both changed Python files with `sonar analyze secrets`, run `git diff --check`, and inspect the complete branch diff against `origin/main`.

#### Step 6: Commit the implementation

```powershell
git add tools/agent-coordination/coordination.py tools/agent-coordination/tests/test_coordination.py docs/superpowers/plans/2026-08-16-doctor-pid-spread.md
git commit -m "fix(coordination): distrust per-session PID spread (#91)"
```

Push the branch and open a PR that closes #91 only after exact-head verification and the repository-required adversarial review.
