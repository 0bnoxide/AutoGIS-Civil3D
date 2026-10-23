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
- `Hooks config SHA-256: <64 lowercase hex digits>`

The verification date records when the inspection and probe were performed.
The initial evidence record uses `2026-08-05`.

`tools/agent-coordination/coordination.py doctor` reads this file and reports
`Codex project-hook trust: verified (YYYY-MM-DD)` only when all the marker
lines are present and valid and the recorded hash matches the SHA-256 of the
primary checkout's `.codex/hooks.json` bytes. A missing file, missing marker,
malformed date or hash, or non-passing result remains an advisory finding
that explains the required recorded checks. A valid record whose hash no
longer matches is reported as stale, with the steps to re-verify.

Codex tracks trust per hook definition, but the record pins the whole file,
so any change to `.codex/hooks.json` marks it stale for review. Linked
worktrees run the primary checkout's copy, so that is the one hashed.

## Error handling

Trust evidence is advisory. An unreadable evidence file is reported as
unverified; it does not make `doctor` fail or weaken the independent main
protection rules.

## Tests

Focused `unittest` coverage will prove the `doctor` output for a missing
record, incomplete/invalid evidence, a valid dated record, and a record
whose hash no longer matches `.codex/hooks.json`. The tests use
temporary repositories and do not target the primary worktree.

## Scope

`doctor` reads `.codex/hooks.json` only to hash it for the staleness check.
It does not infer Codex hook activation or trust from the file's contents,
automate the interactive `/hooks` UI, or alter hook configuration. It only records and verifies the already-required human
evidence.
