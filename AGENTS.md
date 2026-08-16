# AGENTS.md

Thin Codex entrypoint. The canonical operating policy is
[docs/agent-guide.md](docs/agent-guide.md) — read it first; it wins over
anything here. Session lifecycle: [docs/collaboration.md](docs/collaboration.md).

Harness specifics:

- Hooks are wired in the checked-in `.codex/hooks.json` (project trust
  required; verify with `/hooks`): SessionStart refreshes the
  codebase-memory index, PreToolUse denies writes targeting `main` through
  the shared rule engine in `tools/agent-coordination/`. On Windows the
  SessionStart command runs through `tools/agent-hooks/session-start.ps1`,
  which locates Git Bash relative to `git` itself.
- Skills under `.agents/skills/` and agents under `.codex/agents/` are
  rendered from `tools/agent-assets/` — edit the canonical source and run
  `python tools/agent-assets/sync.py`, never the rendered copies.
- For every coding task, automatically invoke the `ponytail` skill at its
  default full intensity. It remains active unless the user explicitly says
  `stop ponytail` or `normal mode`.
- The required Sonar secret scan deliberately authenticates through the host
  OS Keychain. A sandbox-only missing-token result is an execution boundary,
  not a request to reauthenticate: verify with host-scope `sonar auth status`
  (`Source  OS Keychain`) and use that authenticated scanner. This configured
  scan is intentional and needs no per-run authorization.
- Use a stable session id for claims (`--session`); export it as
  `AGENT_SESSION_ID` so `check` finds it.
