# ADR-0013: Run a project orchestrator that merges under ADR-0004

**Status:** Accepted 2026-09-28 by owner approval of the
[project-orchestrator design](../superpowers/specs/2026-09-28-project-orchestrator-design.md)
("Approved, write the plan").

## Context

The owner wants the repository to keep progressing on its vision without
prompting each agent session by hand. The owner's `project-orchestrator`
skill separates a supervisor, workers and independent verifiers, and
accepts work only after an independent PASS on frozen bytes.
[ADR-0004](0004-one-adversarial-review-proportioned-to-risk.md) sets the
merge bar, and the [roadmap](../roadmap.md) reserves gate decisions to the
owner. All agents and the owner act through one GitHub identity, so
authorship alone cannot tell the owner from an agent.

## Decision

Run the skill as a supervisor from Claude or Codex, one at a time, enforced
by a `supervisor` coordination claim. The supervisor:

- dispatches workers from its own harness;
- has each pull request verified by the other harness against the
  `pr-reviewer` contract;
- merges anything that meets ADR-0004, using `gh pr merge
  --match-head-commit` at the verified SHA.

Some changes also need the owner's approval: specs, ADRs, gate changes, and
changes to the rules the orchestrator runs under. The supervisor never
opens a gate. [docs/orchestration.md](../orchestration.md) is the authority
for how the orchestrator operates, including which paths need approval and
which actions are prohibited.

## Alternatives considered

- **Coordinator only, never merging.** Rejected: the owner chose full
  autonomy.
- **An orchestrator program that polls GitHub and launches agents.**
  Rejected: it is the daemon the skill avoids, and it adds code that must
  be maintained and tested.
- **A repository-specific skill written from scratch.** Rejected: it would
  fork from the owner's skill, so improvements to that skill would stop
  reaching this repository.

## Consequences

Plans and implementation merge without the owner. Review is therefore what
protects `main`. Every merged SHA carries its own PASS, which is stricter
than ADR-0004's light tier. The owner-approval paths stop the supervisor
from loosening its own guardrails. Owner attention is requested through a
Q&A post plus a push notification, because GitHub does not notify an
account of its own posts. No phase is authorized.
