# Phase 3 Exit Gate and Phase 4 Authorization — Design

**Status:** Approved 2026-08-13 (owner). This design governs only the
roadmap transition that accepts Phase 3 and authorizes Phase 4. It does not
approve a Phase 4 implementation design or implementation plan.

## Problem

The Phase 3 acceptance bundle defined by the approved
[producer-adoption design](2026-08-11-phase-3-producer-adoption-design.md)
is complete on [issue #78](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/78):
the pinned producer compatibility job is green on `main`, its negative
control proves the validator leg can fail, and the AutoGIS producer feature
is merged under its own ADR. The authoritative
[roadmap](../../roadmap.md) still records Phase 3 as `Authorized`, so its
decision-state no longer reflects the evidence accepted by the owner.

The owner has also approved the transition that would authorize Phase 4 as
the next active phase. That authorization is prospective: it becomes
authoritative, and Phase 4 work becomes claimable, only when the separate
roadmap transition pull request merges. The repository has no approved Phase 4
governing design or implementation plan, so authorization must not be mistaken
for approval to implement the Autodesk adapter foundation.

## Decision

Land one governance-only pull request with these changes:

1. Change the Phase 3 capability row from `Authorized` to `Accepted`.
2. Change the Phase 4 capability row from `Identified` to `Authorized`.
3. Append two rows to the roadmap gate-change log, in decision order:
   - the owner accepted the Phase 3 exit-gate evidence collected on issue
     #78; and
   - the owner authorized Phase 4 while leaving implementation blocked
     until a Phase 4 design and plan are approved.
4. Close issue #78 through the pull request after the roadmap transition
   merges.

Two log rows preserve the distinction between accepting completed work and
opening the next phase. Prior log rows remain byte-for-byte unchanged. The
new rows link to issue #78 as the single evidence bundle instead of copying
its checklist into the roadmap.

Once the roadmap transition pull request merges, Phase 4 authorization will
permit its design and planning work to be claimed. It will not authorize Phase
4 implementation, alter the capability order, or authorize Phase 5 or any
later phase. Because no Phase 4 governing design exists yet, this transition
adds no delivery-level link.

## Change boundary

The gate-transition implementation modifies only `docs/roadmap.md`:

- The capability table owns the two status changes.
- The append-only gate-change log owns the two owner decisions.

The existing Phase 3 design, implementation plans, workflow, compatibility
scripts, fixtures, and pinned AutoGIS commit are historical evidence and do
not change. No ADR is required because the capability order and architecture
remain unchanged.

## Evidence flow

The acceptance flow is deliberately one-way:

1. Issue #78 remains the live evidence bundle.
2. The owner accepts that evidence and authorizes the next phase.
3. The roadmap records the two durable decision-states.
4. Merge of the roadmap pull request closes issue #78.

This keeps live evidence on GitHub and durable gate state in the roadmap,
matching the ownership rules in the [agent guide](../../agent-guide.md).

## Verification

Before review, the branch must prove that the gate log is append-only and
that no unrelated files changed:

```powershell
python tools/checks/docs_checks.py --root . --baseline origin/main
python -m unittest discover -s tools/checks/tests -v
git diff --check
git diff --name-only origin/main...HEAD
```

The documentation checker and its tests must pass, `git diff --check` must
report no errors, and the implementation diff must name only
`docs/roadmap.md`. The immutable Phase 3 evidence should be rechecked against
the successful main-branch workflow run cited on issue #78 before requesting
review. A documentation-only gate-transition pull request is not expected to
rerun the path-filtered compatibility workflow.

After merge, issue #78 must be closed and the merged `main` roadmap must show
Phase 3 as `Accepted` and Phase 4 as `Authorized`.

## Risks and controls

- **Implicit implementation authority:** The Phase 4 log row explicitly
  keeps implementation blocked pending an approved design and plan.
- **Evidence duplication:** The roadmap links issue #78 rather than copying
  its evidence checklist.
- **Audit-history damage:** The change appends rows and never edits or
  reorders prior decisions.
- **Premature issue closure:** Issue #78 closes only when the authoritative
  roadmap transition merges.
- **Accidental later-phase opening:** Phase 5 and every later status remain
  unchanged.

## Exclusions

- Designing or implementing the Autodesk adapter foundation.
- Changing the Phase 3 producer compatibility harness or AutoGIS pin.
- Reopening the Phase 3 implementation scope.
- Authorizing Phase 5 or making Civil 3D runtime claims.
