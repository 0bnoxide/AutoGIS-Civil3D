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
| Orchestrator operation | [docs/orchestration.md](orchestration.md) |

Never write live status (who is working on what, what is in progress) into a
document — GitHub carries it. Never restate another document's list, table,
or count — link it. If two documents disagree, the one in the table above
wins for its row's concern; file an issue for the mismatch. Authority flows
only downward from the table: a fact recorded in an evidence or derived doc
(README, `docs/diagnostics/`) is input to an owner decision, never a license
to widen a claim elsewhere.

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
6. **Use codebase-memory MCP before codebase search.** First query the
   codebase-memory MCP tools before invoking `rg`, `grep`, `Select-String`,
   or other file-by-file search. If the MCP is unavailable or cannot answer,
   use a search subagent before manual search. Pass this repository's
   project (`C-Users-ichbi-AutoGIS-Civil3D` on the primary machine; the
   SessionStart hook prints it) on every call, never another repository's or
   a worktree's — see [agent-tools.md](agent-tools.md). The index is
   advisory — verify anything load-bearing against the files. Session-start hooks refresh it
   and log the outcome to `~/.cache/codebase-memory-mcp/last-index.log`.
7. **Ponytail is automatic for coding tasks.** Before writing, adding,
   refactoring, fixing, reviewing, or designing code, invoke the `ponytail`
   skill at its default full intensity. It remains active unless the user
   explicitly says `stop ponytail` or `normal mode`.
8. **Report only what you verified.** Test counts, hashes, and check results
   in any record must be numbers you produced, not numbers you were told.
9. **Community evidence is pulled, never harvested.** Forum threads, Dynamo
   graphs, and open-source repositories are consulted to answer a specific
   open question in an active design, and an entry is recorded only where
   it changes a decision, a check, a fixture, or a qualification scenario:
   in that design's `Prior art` section, or as a `community-evidence` issue
   when no active design owns it. Link and paraphrase; never copy code into
   this repository, and note the licence of any code source.

## Native desktop testing

Full-desktop capture and control are required only when opening a fresh
Civil 3D instance to locate its separate startup prompt and, with
authorization for that exact prompt and action, dismiss it. Startup dialogs
may belong to child processes or distinct top-level windows, so main-window
capture may miss them. After startup is complete and the intended instance
is verified, normal native testing may use supported app-window capture and
control with related dialogs.

Use only the supported computer-use tools required by the repository and
installed skill for capture and input. Full-desktop scope does not authorize
alternative capture or input APIs.

Before each input, verify the intended target against a fresh screenshot
with the coverage required for that stage; recheck after focus, window or
dialog changes. Confirm the exact dialog/window identity and, when relevant,
its owning executable, PID and process start time. Coordinate one designated
desktop controller across sessions before any input; full-desktop scope does
not release another agent to control the desktop.

Existing authorization, safety rules and stop conditions still apply.
Full-desktop access does not authorize clicking unrelated windows,
dismissing unapproved consequential prompts, accepting terms, changing
security or bypassing licensing/authentication/trust gates. If required
full-desktop startup coverage is unavailable, report the limitation and stop
the dependent startup step; do not silently substitute narrower capture or
claim full coverage. Do not infer the cause of a specific licensing-dialog
issue from targeting mode alone; diagnosis requires separate evidence.

## Reviews

The merge bar, tiers, and the practices for responding to findings are
defined in [ADR-0004](adr/0004-one-adversarial-review-proportioned-to-risk.md).
Mechanics that reach a reviewer: mark the PR ready for review, or comment
`@codex review`. Prose asking for review reaches nobody.

## When blocked

Open or update a GitHub issue describing the blocker, release claims you
cannot use, and stop. Do not improvise around a gate, a failing check, or a
contested claim.
