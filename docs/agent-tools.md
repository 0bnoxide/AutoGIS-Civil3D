# Agent tools: registration, verification, fallbacks

Optional integrations that assist navigation and cross-session context.
Both are advisory: their absence degrades convenience, never correctness,
and no repository check may hard-depend on them.

## codebase-memory (graph index)

- **Registration:** user-scope MCP server (`claude mcp add --scope user`),
  never a committed project `.mcp.json` — no credential or absolute
  executable path belongs in the repository.
- **Project:** one server instance serves every repository on the machine,
  and its query tools take an explicit `project`. Always pass this
  repository's project, the canonical checkout path with `:` dropped and
  separators turned into `-` (`C:/Users/ichbi/AutoGIS-Civil3D` ->
  `C-Users-ichbi-AutoGIS-Civil3D`); the SessionStart hook prints it. Never
  query `C-Users-ichbi-AutoGIS` (the sibling repository, whose name is a
  prefix of ours), a `...-.claude-worktrees-...` / `...-.worktrees-...`
  project, or cross-project tools: their results are not this repository's
  code.
- **Refresh:** the SessionStart hook fast-forwards a clean primary `main`,
  re-indexes the repository, and appends the outcome to
  `~/.cache/codebase-memory-mcp/last-index.log`: `ok`, `FAILED`, or `STALE`
  when tracked files are missing from the graph (the line names the fix).
  A stale index misleads silently; the log line is the diagnosis. The log is
  shared with other repositories' hooks; this repository's lines carry
  `AutoGIS-Civil3D` after the timestamp.
- **Scope:** the index covers the primary checkout, deliberately — the MCP
  keys project identity to the path, so per-worktree indexing would register
  a throwaway project per worktree (rationale in
  `tools/agent-hooks/session-start.sh`). The server still auto-indexes its
  own launch directory, so a worktree session creates such a project anyway;
  ignore it and query the canonical project above.
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
