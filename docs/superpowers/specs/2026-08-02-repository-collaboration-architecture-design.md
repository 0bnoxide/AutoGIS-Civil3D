# AutoGIS-Civil3D Repository Collaboration Architecture

**Date:** 2026-08-02

**Status:** Accepted by owner on PR #1 (2026-08-02) — Phase 0 implementation is gated on a separately approved plan

**Scope:** Repository governance, phased roadmap structure, Claude/Codex collaboration, local-first `main` protection, shared agent tooling, and bootstrap publication

## Context

`AutoGIS-Civil3D` is beginning as a contract-first .NET repository that will later host an Autodesk/Civil 3D adapter. The existing LandXML design correctly keeps the handoff contract and validator independent of ArcGIS and Autodesk assemblies. Before implementing that roadmap, the repository needs a durable structure that lets Claude and Codex work together without direct writes to `main`, branch collisions, duplicated policy, or ephemeral decisions.

At initial drafting, the repository had local commits for the approved LandXML design, its implementation plan, and the `.worktrees/` ignore rule, while GitHub was empty. The diagnostic ZIPs, audit, and repaired source remain untracked. Those artifacts are evidence and must not be silently staged, moved, or deleted during the governance bootstrap.

AutoGIS already demonstrates useful patterns: isolated worktrees, a read-only primary branch, shared claims, cross-harness hooks, repo-local skills, ADRs, phase gates, independent PR review, graph-assisted navigation, and a low-volume Mnemoverse channel. This design selectively ports those behaviors behind neutral interfaces. It does not copy AutoGIS-specific ArcPy, envmon, site, path, or historical machinery.

## Goals

- Establish one authoritative home for roadmap state, architectural decisions, implementation work, agent guidance, and temporary handoffs.
- Define a two-level roadmap that identifies the anticipated product arc while detailing only the active and immediately next phase.
- Make local `main` read-only for Claude, Codex, and ordinary Git commands before server-side branch protection is available.
- Give both harnesses equivalent coordination, skills, agents, and review expectations.
- Default to one writer and one independent reviewer per work item while permitting explicitly disjoint parallel slices.
- Keep all collaboration machinery outside the product dependency graph and ordinary Autodesk runtime.
- Make missing optional tools degrade visibly to documented fallbacks without blocking repository work.
- Publish this design through a draft PR and obtain Claude review before merge.

## Non-goals

This design does not:

- Implement the LandXML contract, validator, CLI, fixtures, AutoGIS producer, or Civil 3D adapter.
- Install the .NET, AutoCAD, ObjectARX, or Civil 3D SDKs.
- Organize or modify the untracked diagnostic evidence.
- Enable GitHub server-side branch protection under the current account plan.
- Create a daemon, hosted coordinator, or security boundary.
- Mirror AutoGIS PR traffic or raw source into Mnemoverse.
- Authorize a later roadmap phase merely by naming it.

## Decision summary

The repository will use a **selective neutral port** of the proven AutoGIS collaboration behavior.

The coordination implementation is one deep module under `tools/agent-coordination/`. Its small interface hides registry locking, Git-common-directory resolution, claim conflicts, branch detection, hook payload parsing, worktree ownership, and diagnostics. Claude, Codex, and Git hooks are adapters at this seam; they do not reimplement policy.

Repository guidance is neutral and canonical under `docs/agent-guide.md`. `AGENTS.md` and `CLAUDE.md` are thin harness entrypoints. Pinned skill and agent sources are maintained under `tools/agent-assets/` and deterministically rendered into the harness discovery paths.

GitHub and committed repository files remain authoritative. Mnemoverse is a pre-artifact context channel only, and codebase-memory-mcp is an advisory navigation index only.

## Sources of truth

| Concern | Authoritative location |
|---|---|
| Capabilities, sequence, gate state, and gate-change log | `docs/roadmap.md` |
| Architectural and structural decisions | `docs/adr/` |
| Approved detailed designs | `docs/superpowers/specs/` |
| Approved implementation plans | `docs/superpowers/plans/` |
| Executable work and published status | GitHub issues and pull requests |
| Cross-agent operating policy | `docs/agent-guide.md` |
| Contribution and checkout procedure | `CONTRIBUTING.md` and `docs/collaboration.md` |
| Pre-issue or pre-PR handoffs and blockers | Mnemoverse domain `collab:autogis-civil3d` |
| Live local branch, worktree, and file ownership | `.agent-state/claims.json` |

Roadmap entries are not task trackers, ADRs are not a second roadmap, Mnemoverse is not a lock, and graph results are not architectural authority.

## Repository shape

```text
README.md
CONTRIBUTING.md
AGENTS.md
CLAUDE.md

docs/
  architecture.md
  architecture-handoff.md
  roadmap.md
  agent-guide.md
  collaboration.md
  agent-tools.md
  adr/
    README.md
    0001-handoff-contract-ownership.md
    0002-agent-collaboration-and-main-protection.md
  superpowers/
    specs/
    plans/

contract/v1/
src/
tests/
fixtures/
diagnostics/
artifacts/

tools/
  agent-assets/
    skills/
    agents/
  agent-coordination/
  verify-agent-tools.ps1
  sync-agent-assets.ps1

.agents/skills/
.claude/
  agents/
  hooks/
  skills/
.codex/
  agents/
  hooks.json
  hooks/
.githooks/
.github/workflows/

.agent-state/       # gitignored shared local registry
.worktrees/         # gitignored linked worktrees
```

Generated skill copies remain checked in so a fresh or remote checkout has the required instructions. Machine credentials, absolute executable paths, generated graph databases, live claims, worktrees, caches, SDK binaries, and secrets are never committed.

`docs/architecture.md` is the repository-level dependency and module map. It links to `docs/architecture-handoff.md`, which remains the LandXML contract-seam deep-dive and owns its detailed producer, contract, validator, and future-adapter rules without duplicating them in the overview.

## Two-level roadmap

### Capability level

`docs/roadmap.md` identifies the anticipated product arc — the phase-by-capability table with each phase's exit-gate outcome — and the identified-capabilities parking lot (items with no implementation authority or sequence until explicitly reviewed). Per the single-copy rule the capability table lives only in [docs/roadmap.md](../../roadmap.md) and is not restated here.

### Delivery level

Only the active phase and the immediately next phase receive detailed slices, dependencies, test evidence, live gates, and acceptance criteria. One phase owns the integration gate at a time. Disjoint slices inside that phase may run in parallel when separately claimed; later phases remain closed.

Roadmap status values are decision-states only, defined in the roadmap's status vocabulary; live work state (underway, blocked, in review) belongs to GitHub. Only an explicit user decision may authorize, advance, reorder, or reopen a phase. The decision is recorded in the roadmap gate-change log; changes to ordering or architecture also receive an ADR.

The tracked LandXML implementation plan does not authorize execution by its presence. Phase 0 is established first, then the owner explicitly opens the appropriate contract phase.

## ADR governance

ADRs record decisions, context, alternatives, and consequences. They do not carry live task status.

The initial sequence is:

- ADR-0001: AutoGIS-Civil3D owns the handoff contract and its dependency direction.
- ADR-0002: Claude/Codex collaboration, neutral agent structure, and local-first `main` protection.

Later ADRs are created only when a phase presents a real decision. The ADR index must distinguish `Proposed`, `Accepted`, `Deprecated`, and `Superseded` states. A roadmap gate change that merely records progress does not need a new ADR; a changed seam, invariant, supported platform, or phase order does.

## Agent operating model

Claude and Codex are peers at repository scope. Each work item assigns one writer and one independent reviewer. Roles may rotate between work items and may be explicitly handed off.

Parallel implementation is permitted only after decomposition into disjoint branch and file scopes. Contract schemas, roadmap gates, ADR indexes, root build configuration, and coordination tooling are serialized even when other work proceeds in parallel. If two slices affect the same interface or integration gate, they are not disjoint.

The reviewer inspects the exact pushed head independently, publishes findings on the PR, and re-reviews after a material head change. Review feedback does not silently transfer write ownership; any role change is explicit in GitHub or, before a GitHub artifact exists, Mnemoverse.

## Checkout and worktree lifecycle

1. Read `AGENTS.md` or `CLAUDE.md`, the canonical agent guide, the active roadmap gate, open GitHub work, and targeted Mnemoverse results.
2. Run the coordination doctor and inspect current claims.
3. Confirm local `main` is clean, then synchronize it only through the approved fast-forward operation.
4. Create an agent branch and linked worktree:

   ```text
   codex/<work-slug>  -> .worktrees/codex+<work-slug>
   claude/<work-slug> -> .worktrees/claude+<work-slug>
   ```

5. Claim the branch, worktree, and intended file globs.
6. Run `check` after entering the worktree and before each write-producing operation to confirm the current branch, worktree, and target remain inside the approved claim.
7. Work and validate only within the approved, claimed scope. Phase 0 claims remain live until explicitly released.
8. Use Mnemoverse only for a necessary pre-artifact status, blocker, or decision pointer.
9. Push and open a draft PR. GitHub becomes authoritative for status and review.
10. After merge or abandonment, release claims and remove the worktree through a validated cleanup procedure.

The cleanup procedure resolves and verifies exact paths, checks for uncommitted work and reparse points, releases claims, and removes only the named linked worktree. It never recursively deletes a computed or broad directory.

## Coordination module

`tools/agent-coordination/` presents this conceptual interface:

```text
init
doctor
status
claim
release
check
sync-main
```

That is the complete Phase 0 command interface. The blocking core is stateless `main` protection, the worktree lifecycle, explicit-release claims with integrity controls, `init` and `doctor`, deterministic agent-asset sync, and the focused proofs described below. Deferred hardening includes temporal claims, `resync`, a general coded break-glass flow, broad corruption-repair automation, exhaustive adapter parity, and broader CI matrices. Deferred commands are not stubbed.

The initial implementation is Python 3 using the standard library only. It is repository-development tooling, not a product dependency; no .NET project or shipped Civil 3D bundle references it. PowerShell, Claude, Codex, and Git integrations remain thin adapters over the same Python rule engine.

Its implementation owns:

- Repository and Git-common-directory discovery.
- Branch, worktree, and target-file resolution.
- Claim conflict and stale-suspect rules.
- Atomic registry writes and an OS-level writer lock.
- Claude, Codex, patch, shell, and Git-hook payload normalization.
- Human-readable diagnostics and machine-stable exit behavior.

### Claim registry

`.agent-state/claims.json` lives at the primary working tree resolved through `git rev-parse --git-common-dir`, not inside each linked worktree. Every worktree therefore observes the same registry.

Claims record session identity, harness, process and host where available, claim kind, branch, worktree, file glob, and start time. Registry mutation takes a local lock, rereads after locking, writes a temporary sibling, and atomically replaces the registry. A contested live resource is rejected. Phase 0 never expires or reaps a claim automatically: `doctor` marks old claims as stale-suspect, and only an owner-authorized `release --force <id> --reason ...` can clear an orphaned claim.

Heartbeat, time-to-live, automatic expiry and reaping, and `resync` automation join the interface only after a demonstrated lifecycle failure shows that explicit release plus `doctor` is insufficient. Any future temporal-claim design must not release or ignore a claim solely because a heartbeat was missed or a TTL elapsed; takeover requires explicit release or positive owner-liveness confirmation plus a recorded takeover handshake.

### Main protection

Protection is layered because the current private GitHub repository cannot use server-side protected branches under the present account plan:

- Claude and Codex pre-tool adapters deny edits, patches, and Git writes whose resolved target is local `main`.
- Repository `pre-commit` and `pre-push` adapters block commits on `main` and pushes whose refspec targets remote `main`.
- `init` installs the repository-local hook path with `core.hooksPath=.githooks` after verifying the target, and `doctor` reports missing or displaced Git hooks.
- `sync-main` is the only ordinary mutation allowed on local `main`; it requires a clean tree and performs a fast-forward-only synchronization from `origin/main`.
- Feature integration occurs through GitHub pull requests, never a local merge into `main`.

The stateless main rule is evaluated independently of dynamic claims. A registry failure cannot turn a main write into an allow. The current private-repository limitation and the future server-protection option are documented against [GitHub's protected-branch availability](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches/about-protected-branches).

### Failure behavior and repair

- Read operations remain available.
- Corrupt or inaccessible claim state blocks new claims and claim-dependent writes with a clear repair instruction.
- `doctor` detects missing hook files, wrong worktree placement, stale-suspect claims, skill drift, branch/upstream errors, and unavailable optional tools. It reports Codex project-hook trust as unverified until the documented `/hooks` inspection and harmless activation probe succeed; it never infers trust from file presence alone.
- Repair preserves damaged state before explicit registry recreation; it never silently deletes it.
- A missing optional graph or Mnemoverse integration is advisory and provides a fallback.
- Phase 0 has no general coded break-glass override or private audit log. Its only coded orphan-claim recovery is the owner-authorized `release --force` behavior described above. Hook or corrupt-registry recovery requires explicit owner authorization and is manual: it preserves or quarantines damaged claim state instead of deleting it, applies only the narrow required repair, restores and verifies protection immediately, and records the reason on the PR or in a Mnemoverse `[DECISION]` pointer. A general coded flow is added only after repeated use demonstrates need.

The coordination module is a guardrail rather than a security boundary. Repository permissions and GitHub remain the external authority.

## Agent guidance, skills, and agents

`docs/agent-guide.md` is canonical. `AGENTS.md` and `CLAUDE.md` contain only harness startup details and links to that guide. Neither entrypoint renames repository paths or creates a separate policy.

Pinned canonical skill sources live in `tools/agent-assets/skills/` and are deterministically rendered into both `.agents/skills/` and `.claude/skills/`. The sync operation cleanly recreates destinations, prunes destination-only files, and supports a check-only mode used by CI.

The initial skill set is:

- `ponytail`
- `ponytail-audit`
- `ponytail-debt`
- `ponytail-gain`
- `ponytail-help`
- `ponytail-review`
- `new-adr`
- `new-issue`
- `pr-doctor`
- `ship`

AutoGIS-specific ArcPy, envmon, site, and run skills are excluded. Ponytail governs code and review work; it never replaces understanding, testing, security, or explicit roadmap gates.

Initial agent behavior contracts are `graph-codebase-navigator` and `pr-reviewer`. Shared behavior lives under `tools/agent-assets/agents/`, while Claude Markdown and Codex TOML definitions are thin adapters validated for equivalent blocking behavior. Contract-specific or Autodesk-specific agents are deferred until repeated work demonstrates leverage.

Codex discovers repository skills under `.agents/skills/` and project-scoped custom-agent TOML files under `.codex/agents/`. Codex project hooks are discovered from `.codex/hooks.json` or inline `[hooks]` tables in `.codex/config.toml`; Phase 0 chooses checked-in `.codex/hooks.json` as its canonical project source. `.codex/hooks/` contains only referenced thin handler scripts and is not itself a discovery surface. Project `.codex/` layers and non-managed hook definitions require trust. Phase 0 verifies discovery with a harmless activation probe and documents `/hooks` inspection in `docs/agent-tools.md`.

Agent-asset updates are pinned and reviewed. No script silently pulls an upstream `latest` into a working branch.

## Codebase graph

`codebase-memory-mcp` is installed and registered at user scope, not vendored into the repository. Its [documented C# resolver](https://github.com/DeusData/codebase-memory-mcp/blob/main/README.md) makes it useful for later .NET navigation and call-path analysis.

At session start, an agent checks index status and refreshes detected changes when the tools are available. The repository supplies a read-only setup and health preflight, including the expected project identity, but commits no executable path, credentials, cache database, or generated graph artifact.

Graph output is advisory. Agents verify relevant results against current source and Git. If tools are missing, stale, or unsupported in a hosted session, they fall back to `rg`, file reads, build output, and Git history. Roadmap and ADR text remains authoritative regardless of graph coverage.

## Mnemoverse communication channel

The dedicated domain is `collab:autogis-civil3d`. It is low-volume cross-agent context, not an ordered mailbox and not a locking mechanism.

At startup, agents perform separate targeted reads for:

```text
STATUS handoff
BLOCKER
DECISION
```

Allowed messages begin with `[STATUS]`, `[BLOCKER]`, or `[DECISION]` and identify sender, recipient, date, branch or artifact, requested action, and any superseded status. Writers check whether the importance gate stored or filtered the message. Superseded entries are marked explicitly; deletion occurs only through user-approved cleanup.

If work has a GitHub issue or PR, status and review discussion belong there. Mnemoverse messages contain pointers, not raw diffs, logs, source, credentials, environment values, customer data, or workstation evidence. Missing Mnemoverse access fails open with a visible fallback to GitHub and repository state.

Mnemoverse is also registered at user scope for each local harness. No project `.mcp.json`, credential, or absolute executable path is committed in Phase 0. `docs/agent-tools.md` records the supported registration and restart procedure, while `verify-agent-tools.ps1` performs read-only availability checks. Hosted sessions use an already-provided connector when present and otherwise follow the documented fallback.

## CI and verification

Ordinary CI runs on Windows without Autodesk, Civil 3D, or ArcGIS installations. Phase 0 blocks on the smallest set that proves its guarantees:

- Registry decision-matrix unit tests for claim, conflict, and release behavior.
- Synthetic main-targeting payload denial through both Claude and Codex adapters.
- One compact disposable-repository path that installs the Git hooks and proves actual commit and direct-push denial on `main`.
- One focused contested-claim path in the disposable repository that proves the writer lock, atomic replacement, and conflict rejection work together.
- Skill and agent-asset sync `--check`.

Broader worktree integration, exhaustive payload-adapter parity, temporal-claim, corrupt-registry-repair, documentation-link, ADR-index, and agent-tool preflight matrices are added to blocking CI only after demonstrated need. Until the document set stabilizes, documentation and tool-availability checks remain visible but non-blocking.

CI proves policy and adapter behavior, but it cannot prove that either harness actually discovers and invokes its project hook before an edit. Phase 0 completion therefore also requires a harness-interception smoke test outside CI: each real harness loads the checked-in project configuration and attempts a harmless sentinel edit targeting `main` in a disposable repository. The probe passes only when the harness denies the action before filesystem mutation. Its evidence is recorded on the Phase 0 PR, includes Codex `/hooks` trust inspection, and is repeated after any hook-configuration change. The real primary worktree is never used as the mutation target for this probe.

As product phases land, CI also performs .NET 8 locked restore, Release build, tests, formatting, contract-fixture conformance, and diagnostic static validation. Live Civil 3D qualification is a separate evidence gate and is never inferred from ordinary CI.

## Bootstrap and publication sequence

At initial written-spec time, local `main` contained four commits and the GitHub repository was empty. Commit `2e12135` is the immutable bootstrap seed. A separate linked worktree uses `feat/landxml-handoff-contract` for LandXML handoff foundation work descended from that seed. Its branch, commits, worktree, and files are preserved and remain outside this PR.

The bootstrap sequence is:

1. Create `codex/repository-collaboration-architecture` in `.worktrees/codex+repository-collaboration-architecture` from current local `main`.
2. Record a pre-PR Mnemoverse status identifying Codex as the spec writer.
3. Add only this approved architecture specification.
4. Self-review and commit the spec on the architecture branch.
5. Push the architecture feature branch first. This uploads its unchanged parent history without targeting remote `main`.
6. Through GitHub's Git-reference API, create `refs/heads/main` at the verified seed commit `2e12135` and set it as the default branch. Verify the remote ref equals the unchanged local seed. No hook override or direct Git push to remote `main` is used.
7. Open a draft PR from the architecture branch to `main`.
8. Request Claude's independent review on GitHub. Because both agents may act through the same GitHub owner identity, Claude records evidence and findings in a PR comment or review note; the workflow does not pretend this is a distinct account's required approval. GitHub becomes authoritative for the review.
9. Address findings, validate the exact head, and request re-review after material changes.
10. Merge only after Claude review and owner approval.
11. Fast-forward local `main`. Write the Phase 0 implementation plan from a new isolated worktree after the design is accepted.

The untracked diagnostics are never staged as part of the design PR.

## Phase 0 acceptance criteria

Phase 0 is complete only when:

- `README.md`, `CONTRIBUTING.md`, roadmap, ADR index, architecture, collaboration, and agent guides agree.
- The two-level roadmap includes explicit authorization and gate-change rules.
- ADR-0001 and ADR-0002 record the contract and collaboration decisions.
- Claude and Codex load equivalent pinned skills and agent behavior.
- Registry decision-matrix tests and the focused contested-claim integration path pass.
- Blocking CI proves synthetic main-targeting payload denial through both adapters and real Git-hook denial of commits and direct pushes to remote `main` in a disposable repository.
- Each real harness passes the separately recorded interception smoke test, including Codex project-hook trust activation, before Phase 0 is accepted and after hook-configuration changes.
- The normal claim, conflict, and explicit-release flow passes; `doctor` reports stale-suspect claims without expiring them, and cleanup remains a documented validation-first procedure.
- Graph and Mnemoverse preflights work or produce documented fallbacks.
- Windows CI passes the focused main-protection, registry, and agent-asset checks without Autodesk dependencies.
- Claude independently reviews the exact final foundation head.
- Diagnostic evidence is preserved and only organized in its authorized implementation slice.

## Consequences

### Positive

- Both agents follow one policy and one coordination rule engine.
- Neutral repository paths avoid making either harness architecturally primary.
- Local enforcement covers the private-repository period before GitHub protection is available.
- Phase gates prevent a detailed plan from becoming accidental authorization.
- Pinned assets make fresh and remote sessions reproducible.
- Product code remains free of agent, MCP, Python-tooling, and Autodesk bootstrap concerns.

### Negative

- The repository carries non-product Python and hook tooling that must be tested.
- Harness adapters and generated skill copies require parity checks.
- Local protection depends on initial setup and hook trust; it is not equivalent to GitHub server enforcement.
- The claim registry can temporarily block writes when damaged, requiring an explicit repair.
- Mnemoverse lifecycle discipline cannot be mechanically enforced as strongly as Git or CI.

## Alternatives considered

### Exact AutoGIS mirror

Rejected because it would copy Claude-biased paths, AutoGIS environment assumptions, ArcPy-specific hooks, and accumulated historical complexity into a new .NET repository.

### Shared external coordination toolkit

Deferred because a separately versioned tool introduces bootstrap, trust, availability, and upgrade problems before this repository has shipped its first product slice. The neutral module can be extracted later if a second consumer proves the seam.

### Conventions without enforcement

Rejected because advisory branch naming and worktree prose do not prevent the direct-main and collision failures this design exists to address.

## Review and implementation gate

This specification is the complete architecture proposal produced by review discussion consensus. It does not authorize Phase 0 implementation until the amended spec is re-reviewed on its exact PR head and the owner accepts that head. After acceptance, a separate detailed implementation plan will decompose Phase 0 into small, independently verifiable commits.
