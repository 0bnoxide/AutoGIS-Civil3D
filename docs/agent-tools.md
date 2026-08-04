# Agent tools: registration, verification, fallbacks

Optional integrations that assist navigation and cross-session context.
Both are advisory: their absence degrades convenience, never correctness,
and no repository check may hard-depend on them.

## codebase-memory (graph index)

- **Registration:** user-scope MCP server (`claude mcp add --scope user`),
  never a committed project `.mcp.json` — no credential or absolute
  executable path belongs in the repository.
- **Refresh:** the SessionStart hook re-indexes the repository and appends
  the outcome (or failure) to `~/.cache/codebase-memory-mcp/last-index.log`.
  A stale index misleads silently; the log line is the diagnosis.
- **Scope:** the index covers the primary checkout, deliberately — the MCP
  keys project identity to the path, so per-worktree indexing would register
  a throwaway project per worktree (rationale in
  `tools/agent-hooks/session-start.sh`).
- **Fallback:** Grep/Glob search, or a search subagent. Treat graph results
  as advisory pointers; verify anything load-bearing against the files.

## Mnemoverse (cross-session context channel)

- **Registration:** user-scope, keyed by `MNEMOVERSE_API_KEY` in the
  session environment. Context channel only: it never locks, claims, or
  records anything authoritative — GitHub and committed files remain the
  record.
- **Fallback:** when absent, cross-session context flows through GitHub
  issues, PR comments, and the handoff sections of the documents in
  [docs/agent-guide.md](agent-guide.md)'s sources-of-truth table.

## Verification

`tools/verify-agent-tools.ps1` performs read-only availability
checks and prints one line per tool: available (with version or index
status) or the documented fallback. It never fails the session and makes no
writes. `doctor` reports the same availability as advisory findings.

After registering or updating either tool, restart the harness session so
the MCP server list reloads, then run the verifier.
