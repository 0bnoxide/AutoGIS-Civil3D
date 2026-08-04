# CLAUDE.md

Thin Claude Code entrypoint. The canonical operating policy is
[docs/agent-guide.md](docs/agent-guide.md) — read it first; it wins over
anything here. Session lifecycle: [docs/collaboration.md](docs/collaboration.md).

Harness specifics:

- Hooks are wired in `.claude/settings.json`: SessionStart refreshes the
  codebase-memory index (outcome logged to
  `~/.cache/codebase-memory-mcp/last-index.log`); PreToolUse denies writes
  targeting `main` through the shared rule engine in
  `tools/agent-coordination/`.
- Skills under `.claude/skills/` and agents under `.claude/agents/` are
  rendered from `tools/agent-assets/` — edit the canonical source and run
  `python tools/agent-assets/sync.py`, never the rendered copies.
- Use a stable session id for claims (`--session`); export it as
  `AGENT_SESSION_ID` so `check` finds it.
