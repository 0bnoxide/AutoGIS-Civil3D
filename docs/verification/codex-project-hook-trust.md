# Codex project-hook trust verification

Verification date: 2026-10-09
Hooks inspection: passed
Activation probe: passed
Hooks config SHA-256: db433f8a218df278f4123f16e06def52e19ec9c1674746c1fd547c4189d3587f
Verified commit: c21a17b7e89a89e628dc5b7c48347ad3ec6d7c51

The date is UTC. The [owner-authorized verification on issue #200](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/200#issuecomment-6072676780)
records a same-session `/hooks` inspection showing the project `PreToolUse`
entry loaded, enabled, and Trusted. Its registered definition was unchanged
when the clean primary checkout was fast-forwarded to the verified commit.
The same session then made one ordinary shell-tool activation attempt with
the prepared disposable `main` repository as its requested workdir. The
authentic `PreToolUse` response denied it with `main is read-only`. At
`2026-10-09T01:54:49Z`, the sentinel was absent in both the fixture and primary
checkout, and the fixture still contained only `.git`.

The denial resolved the relative output target against the primary checkout.
This records `PreToolUse` activation; fixture workdir handling remains
unverified. Actual hook payload fields and hook-child marker booleans were
unavailable, so PR176 runtime qualification remains unverified.

The hash covers the primary checkout's `.codex/hooks.json` bytes at the
verified commit and was unchanged after the probe. Codex tracks trust per
hook definition; changing another entry does not untrust an unchanged hook.
`coordination.py doctor` uses the complete file hash to detect a stale
verification record. After a hook-config change merges, inspect `/hooks`
from the primary checkout, review changed definitions as needed, run an
owner-authorized activation probe, and record the new evidence, date, and
hash here.
