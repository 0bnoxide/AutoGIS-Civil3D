# Doctor session PID-spread design

Issue [#91](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/91)
shows that Codex command wrappers make a live session appear to own several
already-dead processes. `doctor` currently evaluates each claim in isolation,
so it reports those claims as safe to force-release even while their session is
active.

## Decision

`doctor` will treat multiple distinct PIDs recorded by one session on the local
host as evidence that the session's PID signal is unreliable. All claims in
that host-and-session group will then follow the existing unknown-PID path:
old claims are reported as stale suspects, while newer claims produce no
liveness finding.

The PID spread uses positive integer PID values, including string values that
the existing liveness probe accepts as integers. Claims without a usable
session identifier do not participate in grouping. ADR reservations remain
exempt from liveness reporting.

A session with one consistent PID keeps the current behavior. If that PID is
dead, each ordinary claim is reported as orphaned. Sessions are evaluated
independently, and foreign-host claims cannot make a local session's PID signal
unreliable.

## Implementation boundary

The change belongs only in `cmd_doctor`, where claims are already loaded and
the local host is known. It will derive the unreliable local session set once,
then bypass the dead-PID orphan branch for records in that set. The existing
age calculation and stale-suspect message remain unchanged.

Claim creation, process ancestry discovery, registry shape, lock-holder
liveness, release authorization, and CLI output formats outside this fallback
do not change. No harness executable names or new configuration are added.

## Validation

Focused doctor tests will prove that:

- one session with distinct local PIDs does not produce orphan findings and
  uses stale-suspect reporting when old;
- repeating one dead PID within a session still produces orphan findings;
- one dead PID in each of two sessions still produces orphan findings; and
- foreign-host PID spread does not suppress a local dead-PID finding.

The existing single-claim, unknown-PID, ADR, live-PID, and lock-holder tests
remain the compatibility checks. The complete coordination test suite and
repository-required checks must pass before publication.

## Alternatives rejected

Adding known Codex wrapper executable names to `_TRANSIENT_ANCESTORS` is
fragile because wrapper names can change or differ by harness version.
Disabling PID liveness for every Codex claim discards useful evidence even
when a session records one stable process. Session PID spread uses the observed
failure signal and preserves existing behavior everywhere else.
