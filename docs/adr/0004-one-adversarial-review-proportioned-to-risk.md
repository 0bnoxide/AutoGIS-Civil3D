# ADR-0004: One adversarial review before merge, proportioned to risk

**State:** Accepted (owner decision, 2026-08-04)

**Date:** 2026-08-04

## Context

[ADR-0002](0002-agent-collaboration-and-main-protection.md) established one writer and one independent reviewer per work item, reviewing the exact pushed head. In practice across PRs #3–#7 this degraded into a review round per commit. A documentation-only change to a single 60-line file produced six commits and eleven PR comments, because each push triggered a fresh automated review pass and each finding was answered with rationale, a thread resolution, and a head-change summary. The ceremony earned its keep once — review of PR #3 caught a real LandXML issue-selection defect that tests did not — and cost far more than it returned on prose.

Two constraints shape the correction.

Strict writer/reviewer rotation between Claude and Codex is not affordable at current usage limits, so roles cannot be rigidly alternated per work item.

Agent-to-agent review is not continuous. `chatgpt-codex-connector` is a separate model triggered by marking a pull request ready for review or by an `@codex` mention; a thumbs-up reaction is its positive verdict. Other Codex sessions work on the owner's prompting and do not observe pull-request prose addressed to them. Prose in a PR comment asking an agent for review does not summon one.

## Decision

**The merge bar is at least one substantial adversarial review of the change before merge.** Meeting that bar is the requirement. The number of review rounds is not.

Review depth is proportioned to risk:

- **Full review** — product code, contract schemas, issue codes, fixtures, CI configuration, and coordination tooling. An independent reviewer verifies the exact pushed head, runs the suite, and records evidence; findings are dispositioned before merge.
- **Light review** — governance and documentation: roadmap, ADRs, guides, READMEs. One adversarial pass plus green checks is sufficient. No second review round, no per-finding rationale, no head-change narration. The pass itself is not optional: a light-tier change still requires a reader other than its author.

In the light tier the merged head need not equal the reviewed head, provided the delta between them is confined to addressing that pass's findings. Any change beyond that scope — new content, a different decision, an unrelated fix — restarts the bar and requires a fresh pass. In the full tier the reviewed head and the merged head must be the same commit.

Practices that apply to both tiers:

- Batch fixes. Address every finding from a review pass in one push, not one push per finding.
- Answer a finding in one line unless it is contested or a real disagreement is worth recording.
- Request review by a mechanism that reaches a reviewer: mark the PR ready for review, or mention `@codex`. When neither reaches a reviewer — the connector was unavailable for this repository on the day this ADR was written, see issue #11 — the bar does not lower. The owner is the reviewer of last resort, and the PR waits rather than merging unreviewed.
- Roles need not alternate per work item. The same agent may write consecutive items. What the bar requires is that the review come from a perspective other than the one that wrote the change, not that authorship rotate.
- Any bug or issue discovered during a work item is opened as a GitHub issue and tracked, whether or not it belongs to the current task.

## Alternatives

- **Keep uniform full review.** Rejected: it spends the review budget on prose and starves the changes that need scrutiny. The observed cost was six commits and eleven comments for one documentation file.
- **Drop pre-merge review and rely on tests and runners.** Rejected: the defects review actually caught — a wrong primary-issue selection policy, a governance rule that contradicted its own vocabulary — are invisible to a test suite.
- **Require strict role rotation.** Rejected as currently unaffordable. The one-review floor preserves the independent perspective without mandating who supplies it.
- **Tier by file path automatically.** Deferred: a mechanical rule invites gaming the path rather than judging the risk. Revisit if tier assignment becomes a point of dispute.

## Consequences

Governance changes move at the speed of a single pass. Code keeps its scrutiny.

The floor is a floor, not a target. If defects begin reaching `main`, the correction is to raise the tier of whatever drifted, not to add rounds uniformly. Drift is judged by what reaches `main`, not by review volume.

Because the light tier removes the second review *round* — not the second reader — a governance document gets exactly one outside pass, and that pass must read it against its own stated rules rather than as prose. The PR #7 defects were all of that kind: a miscounted reference, a status contradicting the vocabulary that defined it, a log row out of sequence, and a task-tracker line in a file that forbids task tracking. None would be caught by reading for sense.

This ADR supersedes three things in [ADR-0002](0002-agent-collaboration-and-main-protection.md), and nothing else:

1. **Role rotation, both tiers.** ADR-0002 assigns one writer and one independent reviewer per work item with roles rotating. Rotation is no longer required; the same agent may write consecutive items. The reviewing perspective must still differ from the writing one.
2. **Exact-head review, light tier only.** ADR-0002 requires review of the exact pushed head. Batching fixes after a single pass necessarily means the merged head is not the reviewed head, so in the light tier that requirement is replaced by the bounded rule in the Decision: the delta must be confined to addressing the pass's findings.
3. **The implication that every work item carries a distinct reviewer role.**

Main protection, worktree isolation, and exact-head review within the full tier are unchanged.
