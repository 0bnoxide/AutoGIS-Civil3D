# Doctor liveness for same-host dead-pid orphans

**Date:** 2026-08-07
**Issue:** #36

## Purpose

`doctor` decides claim staleness on age alone (`STALE_SUSPECT_HOURS = 24`),
so a claim whose owning process is provably dead — pid gone, same machine —
hard-blocks its branch for up to 24 hours while `doctor` reports nothing.
Claim records already store `pid` and `host`; consume them so a certain
orphan is reported immediately, at any age. Same for the registry lock file,
which records `pid@host` but whose finding today only tells the operator to
"verify the holder is alive" by hand.

Visibility only: enforcement (`claim_denial`) and release semantics are
untouched. The never-reap invariant stands — `doctor` reports, and only an
explicit `release --force --id <id> --reason ...` clears an orphan.

## Design

### Liveness probe

A liveness result is `True` (alive), `False` (dead), or `None` (unknown).
`None` and any probe failure fall back to today's age-only behavior;
`doctor` is advisory and must never crash or misfire a signal.

- **Windows:** one batched `tasklist /FO CSV /NH` call per `doctor` run
  (subprocess with timeout, `errors="replace"`, same defensive pattern as
  the sync-script check). Parse the PID column of CSV rows into a set of
  live pids; membership answers every claim. Any spawn/parse failure →
  `None`. `os.kill(pid, 0)` is banned here: on Windows it maps to
  `TerminateProcess` and would kill a live holder instead of probing it.
- **POSIX:** `os.kill(pid, 0)` — `ProcessLookupError` → dead,
  `PermissionError` → alive (other user), success → alive.

Known ceiling (marked with a `ponytail:` comment at the helper): pid reuse
can make a dead holder look alive. Acceptable — `doctor` is advisory and the
24 h age check still catches it; upgrade path is `OpenProcess` +
creation-time comparison if it ever matters.

### Doctor claim loop

For each non-`adr` claim (ADR number reservations are meant to outlive the
claiming process and stay exempt, as today):

- `record["host"]` equals the local hostname **and** the probe says dead →
  new finding at any age:
  `orphaned claim <id> (<kind>=<value>, session <session>) — pid <pid> is
  dead on this host; release --force --id <id> --reason ... to clear`
- Alive, unknown, or another host → the existing age-based stale-suspect
  finding, unchanged. A dead-looking pid on another host proves nothing.

### Registry lock finding

The lock file already records `<pid>@<host> <timestamp>`. When a lock is
present, parse that line; if the host is local, extend the finding with the
probe result — holder pid dead (safe to remove) or holder alive (hands
off). Unparseable content, foreign host, or unknown probe result keeps
today's neutral "verify the holder is alive before removing manually"
wording. This matters more than a single orphaned branch claim: a dead
lock holder stalls every registry operation on the machine.

### Docstring

The module-docstring invariant ("`doctor` reports stale-suspect claims")
gains the new capability: `doctor` reports stale-suspect claims and
same-host dead-pid orphans; only an explicit `release --force` clears them.

## Out of scope

- Auto-release or reaping of orphans (violates the never-reap invariant).
- Liveness checks in the enforcement path (`claim_denial` stays fast,
  stateless, and conservative).
- A `doctor --fix` flag.

## Error handling

The probe never raises out of `doctor` and never signals a real process on
Windows. Probe failure degrades to age-only reporting, mirroring how a
broken sync script becomes a finding rather than an abort.

## Tests

Focused `unittest` coverage via the existing `doctor_output()` harness,
using temporary repositories:

1. Same-host claim with a dead pid (spawn `sys.executable -c pass`, wait,
   reuse its pid) → "orphaned claim" finding at age ≈ 0.
2. Same-host claim with `os.getpid()` → no orphan finding.
3. Foreign-host claim with a dead pid → no orphan finding; age logic only.
4. Probe forced to fail/unknown → `doctor` completes, age-only findings.
5. `adr` claim with a dead pid → exempt, no orphan finding.
6. Lock file with a dead local holder → finding names the dead pid; with a
   live holder → finding reports the holder alive.
