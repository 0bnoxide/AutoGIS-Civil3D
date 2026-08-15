# Phase-Aware Documentation Gate — Design

**Status:** Approved 2026-08-14 (owner). Governs issue
[#89](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/89).

## Problem

`tools/checks/docs_checks.py` still carries the retired Phase 0 paper gate.
It searches the roadmap for the literal phrase `may not be claimed until`
and, when present, rejects every changed path outside `docs/`. Its diagnostic
is also hard-coded to Phase 0.

Reusing that switch for Phase 4 would block unrelated maintenance of accepted
deliverables and repository tooling. Leaving it inactive provides no
mechanical enforcement once a Phase 4 design identifies the implementation
surface. The checker needs a phase-scoped boundary without guessing intent
from every non-documentation path.

## Decision

Replace the phrase-triggered global gate with a strict machine marker owned by
the authoritative roadmap. The infrastructure remains inactive until an
owner-approved phase design reserves concrete implementation path prefixes.

The marker is a single-line HTML comment placed beside the capability table,
outside the append-only gate-change log:

```html
<!-- docs-checks:phase-gate-v1 {"phase":4,"state":"blocked","paths":["src/AutoGIS.Civil3D.Adapter/","tests/AutoGIS.Civil3D.Adapter.Tests/"]} -->
```

The sample paths illustrate the schema only. This design does not select or
reserve Phase 4 paths and does not add an active marker to the roadmap.

## Marker contract

The payload is JSON with exactly these fields:

- `phase`: a positive integer;
- `state`: the literal string `blocked`; and
- `paths`: a non-empty array of unique repository-relative directory
  prefixes.

Each path uses forward slashes, ends in `/`, and contains no empty segment,
`.` or `..` segment, backslash, absolute-path prefix, or glob metacharacter.
Paths are matched against Git's repository-relative changed filenames.

The roadmap may contain zero or one marker. Zero means that no mechanically
reserved phase implementation surface is active. Duplicate markers,
unrecognized fields, invalid JSON, or an invalid field value are blocking
documentation findings rather than reasons to disable the gate.

## Lifecycle

1. A phase is authorized for design and planning through the normal owner
   decision recorded in the roadmap.
2. The approved phase design defines its implementation boundary and adds the
   marker with those reserved path prefixes in a documentation-only pull
   request.
3. While the marker is active, changes under the reserved prefixes are
   blocked. Unlisted maintenance, tooling, governance, design, planning, and
   documentation paths continue through normal review and CI.
4. After the implementation plan is approved, a documentation-only roadmap
   change removes the marker and records the authorization decision through
   the normal append-only provenance mechanism.
5. Implementation starts only after the marker-removal change merges.

The marker is current gate state, not history, so adding or removing it does
not rewrite the gate-change log.

## Evaluation

The checker evaluates both the merge-base roadmap and the current roadmap.
It parses each marker before examining the diff and enforces the union of
reserved paths from both versions.

That union closes three same-change bypasses:

- adding a marker while also adding implementation under its paths;
- removing a marker while also adding implementation; and
- narrowing or moving reserved paths while modifying paths protected by the
  prior marker.

For an active reserved prefix, a changed file matches when its Git path starts
with that prefix. A finding names the phase and the matching files. Unlisted
non-documentation files are not findings.

If the requested baseline or merge-base roadmap cannot be resolved, gate
evaluation fails closed with a blocking diagnostic. It must not silently
return a clean result because removal-bypass protection depends on the
baseline marker.

## Components and change boundary

The implementation changes only:

- `tools/checks/docs_checks.py`, replacing the Phase 0 phrase switch with
  marker parsing, validation, and scoped evaluation; and
- `tools/checks/tests/test_docs_checks.py`, replacing the global-gate fixtures
  with phase-aware contract and lifecycle tests.

The existing CI command remains unchanged. No GitHub label, pull-request
template, workflow, dependency, roadmap marker, or Phase 4 implementation
path is added by the infrastructure change.

## Verification

Tests must prove:

- no marker leaves unrelated changes unaffected;
- a documentation-only marker addition passes;
- a matching reserved-path change is blocked with a phase-specific message;
- unrelated non-documentation maintenance passes while the marker is active;
- documentation-only marker removal passes;
- marker removal plus a reserved-path change in the same branch is blocked;
- changing reserved paths enforces the union of baseline and current paths;
- an already-removed marker permits later implementation changes;
- malformed, duplicate, and schema-invalid markers fail closed; and
- an unavailable baseline fails closed.

The complete documentation-check suite, repository-proportioned Python and
.NET suites, `git diff --check`, and changed-file inspection remain the merge
evidence. Tests may use synthetic phase and path names; activation for Phase 4
requires its separate approved design.

## Risks and controls

- **Over-broad reservation:** The governing phase design must justify every
  path. The checker never invents or expands prefixes.
- **Gate-removal bypass:** Baseline/current union enforcement prevents code
  from riding with marker removal.
- **Malformed policy disables enforcement:** Strict parsing fails closed.
- **Maintenance blockage:** Only explicitly reserved prefixes are blocked;
  all unlisted paths remain available.
- **Roadmap authority drift:** The marker lives in the roadmap, while the
  phase design owns the path-selection rationale and the gate log owns the
  owner decisions.

## Exclusions

- Selecting or creating the Autodesk adapter foundation.
- Adding an active Phase 4 marker before its design defines the boundary.
- Classifying pull requests with labels or free-form intent declarations.
- Reopening issue #78 or changing the merged Phase 3 exit transition.
- Changing issue #81 or pull request #88.
- Authorizing Phase 4 implementation, Phase 5, or any later capability.
