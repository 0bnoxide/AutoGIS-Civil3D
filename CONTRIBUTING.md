# Contributing

Rules for landing changes. The operating policy for agents is
[docs/agent-guide.md](docs/agent-guide.md); the session lifecycle is
[docs/collaboration.md](docs/collaboration.md).

## Integration

- All changes reach `main` through pull requests. Direct commits and pushes
  to `main` are denied by hooks; local `main` mutates only via the
  coordination tool's `sync-main`.
- Branch naming: `<agent-or-author>/<slug>`, worktrees under
  `.worktrees/<agent>+<slug>`.
- A PR that adds an ADR uses an allocated number
  ([agent guide](docs/agent-guide.md), rule 5) and updates
  [docs/adr/README.md](docs/adr/README.md).

## Review and merge

The merge bar and tiers are defined in
[ADR-0004](docs/adr/0004-one-adversarial-review-proportioned-to-risk.md):
at least one substantial adversarial review from a perspective other than
the writer's, with depth proportioned to risk. Request review by marking
the PR ready for review or commenting `@codex review`.

CI must be green. Blocking checks: the .NET build/test/format job, the
coordination and asset-sync test suites, and the documentation checks
(`tools/checks/docs_checks.py`) — link resolution, no point-in-time state in
living documents, no counting of referenced lists, append-only gate log, and
the roadmap implementation gate.

## Evidence

Any counts, hashes, or results you cite in a PR must be ones you produced.
Diagnostic artifacts under `artifacts/` and `diagnostics/` are preserved
evidence: never staged, moved, or deleted as a side effect of other work.
