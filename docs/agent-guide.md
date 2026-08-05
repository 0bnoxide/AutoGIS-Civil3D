# Agent guide

Canonical operating policy for every agent working in this repository.
`CLAUDE.md` and `AGENTS.md` are thin entrypoints that link here; if either
states a rule that conflicts with this file, this file wins.

## Sources of truth

| Concern | Authoritative location |
|---|---|
| Capabilities, sequence, gate state, gate-change log | [docs/roadmap.md](roadmap.md) |
| Architectural and structural decisions | [docs/adr/](adr/README.md) |
| Approved designs | [docs/superpowers/specs/](superpowers/specs/) |
| Approved implementation plans | [docs/superpowers/plans/](superpowers/plans/) |
| Executable work and live status | GitHub issues and pull requests |
| Checkout and worktree procedure | [docs/collaboration.md](collaboration.md) |
| Contribution and review rules | [CONTRIBUTING.md](../CONTRIBUTING.md) |

Never write live status (who is working on what, what is in progress) into a
document — GitHub carries it. Never restate another document's list, table,
or count — link it. If two documents disagree, the one in the table above
wins for its row's concern; file an issue for the mismatch.

## The rules

1. **`main` is read-only.** Branch first, always. Enforced three ways: Git
   hooks (`.githooks/`), harness PreToolUse adapters, and PR-only
   integration. The only sanctioned local-main mutation is
   `python tools/agent-coordination/coordination.py sync-main`.
2. **Claim before you write.** Follow the numbered procedure in
   [docs/collaboration.md](collaboration.md). A contested claim is a stop
   sign, not an obstacle to route around.
3. **One phase at a time.** Work only inside the roadmap gate that is open,
   or on the maintenance the roadmap exempts from a gate. Authorizing,
   advancing, or reopening a phase is an owner decision recorded in the
   roadmap gate-change log.
4. **Every discovered bug becomes a GitHub issue**, whatever you were doing
   when you found it, whether or not it is yours. If you cannot file it,
   record it as an explicitly marked unfiled item in your handoff.
5. **ADR numbers are allocated, never guessed:**
   `python tools/agent-coordination/coordination.py claim --session <id> --kind adr`
   prints your number. An allocated number is consumed even if unused.
6. **Prefer indexed navigation over manual search.** Use the
   codebase-memory MCP tools or a search subagent before file-by-file
   grep. The index is advisory — verify anything load-bearing against the
   files. Session-start hooks refresh it and log the outcome to
   `~/.cache/codebase-memory-mcp/last-index.log`.
7. **Report only what you verified.** Test counts, hashes, and check results
   in any record must be numbers you produced, not numbers you were told.

## Reviews

The merge bar, tiers, and the practices for responding to findings are
defined in [ADR-0004](adr/0004-one-adversarial-review-proportioned-to-risk.md).
Mechanics that reach a reviewer: mark the PR ready for review, or comment
`@codex review`. Prose asking for review reaches nobody.

## When blocked

Open or update a GitHub issue describing the blocker, release claims you
cannot use, and stop. Do not improvise around a gate, a failing check, or a
contested claim.
