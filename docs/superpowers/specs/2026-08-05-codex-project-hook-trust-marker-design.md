# Codex project-hook trust marker

**Date:** 2026-08-05

## Purpose

Make the `doctor` trust result evidence-based. Codex project-hook trust is
verified only when a reviewed, checked-in record states that both required
real-harness checks passed; file presence alone is insufficient.

## Design

The checked-in record is `docs/verification/codex-project-hook-trust.md`.
It contains these exact, human-readable marker lines:

- `Verification date: YYYY-MM-DD`
- `Hooks inspection: passed`
- `Activation probe: passed`

The verification date records when the inspection and probe were performed.
The initial evidence record uses `2026-08-05`.

`tools/agent-coordination/coordination.py doctor` reads this file and reports
`Codex project-hook trust: verified (YYYY-MM-DD)` only when all three marker
lines are present and valid. A missing file, missing marker, malformed date,
or non-passing result remains an advisory finding that explains the required
recorded checks.

## Error handling

Trust evidence is advisory. An unreadable evidence file is reported as
unverified; it does not make `doctor` fail or weaken the independent main
protection rules.

## Tests

Focused `unittest` coverage will prove the `doctor` output for a missing
record, incomplete/invalid evidence, and a valid dated record. The tests use
temporary repositories and do not target the primary worktree.

## Scope

This change does not attempt to infer Codex hook activation from
`.codex/hooks.json`, automate the interactive `/hooks` UI, or alter hook
configuration. It only records and verifies the already-required human
evidence.
