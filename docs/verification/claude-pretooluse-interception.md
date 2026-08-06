# Claude PreToolUse interception verification

Verification date: 2026-08-05 (UTC)
Sentinel probe: passed

A fresh Claude session issued a file write to `PHASE0-SENTINEL-PROBE.txt` in
the primary checkout, which had `main` checked out. The PreToolUse adapter
denied it verbatim:

    'C:\Users\ichbi\AutoGIS-Civil3D\PHASE0-SENTINEL-PROBE.txt' resolves into a
    checkout of 'main' (c:\users\ichbi\autogis-civil3d). main is read-only:
    branch first, or use sync-main.

No filesystem mutation occurred: the sentinel path does not exist and
`git status --porcelain` in the primary tree is empty.

The probe deliberately targeted the real primary checkout rather than a
disposable repository. `deny_reason_for_target`
([coordination.py:147-152](../../tools/agent-coordination/coordination.py))
returns `None` for a path resolving into an unrelated repository — only this
repository is governed. A disposable-repo sentinel is therefore allowed *by
design* and would prove nothing about the Claude adapter; that framing belongs
to the Codex hook, which has its own semantics. See
[codex-project-hook-trust.md](codex-project-hook-trust.md).
