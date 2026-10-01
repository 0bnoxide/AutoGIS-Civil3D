# Codex project-hook trust verification

Verification date: 2026-08-05
Hooks inspection: passed
Activation probe: passed
Hooks config SHA-256: 8ddadbc0f94b9279dab254d5266c1a13dcd31cbbaafa30aae6ea8bff1afcd517

The verification date is UTC (the session ran the evening of 2026-08-04
-0600). A real Codex session ran `/hooks` inspection against the checked-in
`.codex/hooks.json` and confirmed the project hooks were loaded and active.
The same session then ran a harmless activation probe: a sentinel edit
targeting `main` in a disposable repository, which the hook denied before any
filesystem mutation occurred. Trust was not inferred from file presence.

The hash is the SHA-256 of the `.codex/hooks.json` bytes that inspection
covered (the file as of commit `cf73d08`). Codex keys trust by path and
content hash and silently skips changed hooks until they are re-trusted, so
`coordination.py doctor` reports this record as stale once the primary
checkout's file no longer matches. Linked worktrees run the primary
checkout's `.codex/hooks.json`, so a hook-config change cannot be verified
from a branch: after it merges, re-trust it in `/hooks` from the primary
checkout, rerun the activation probe, and update the date and hash here.
