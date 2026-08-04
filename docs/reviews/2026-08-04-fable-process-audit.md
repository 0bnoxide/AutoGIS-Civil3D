# Fable process audit: root causes of the governance-document defect pattern

**Date:** 2026-08-04

**Provenance:** Independent cold audit by a Fable 5 agent with no session context, commissioned by the owner after ~21 review findings landed against governance documents across PRs #4–#10 in one day, nearly all self-inconsistency defects by the same author (Claude). The auditor read the documents on `main`, the three open claude branches, and the full PR comment record; it made no repository changes. Report reproduced verbatim below; the disposition section at the end is by the audited author.

---

**Headline verdict:** the ~21 governance findings are not a carefulness problem. They are the predictable output of a document architecture that duplicates facts across 4–7 locations with zero mechanical enforcement, produced through a same-day pipeline of concurrent PRs that reference each other's unmerged content. The .NET code drew only judgment findings for a structural reason: the compiler, 181 tests, locked restore, and CI absorb the self-consistency defect class before a reviewer ever sees it. The governance docs have no equivalent — the spec explicitly defers documentation-link and ADR-index CI checks as "visible but non-blocking… until the document set stabilizes," during the exact window when the document set was the only thing under construction. Same author, same day, defect rate tracks the checker gap, not competence.

## A. Cross-reference fan-out and duplication map

**Fan-out for one statement.** For "Phase 0 is authorized; plan-writing is claimable; implementation is not" to be correct, all of the following must agree: the roadmap capability-table Status cell, the status vocabulary in the same file, the capability-level prose, the delivery-level entry, the gate-change log row, the spec's bootstrap step 11, the spec's acceptance-criteria list, ADR-0003's sequencing claim, and the PR #7 body — 6–8 agreement points for a single gate fact. Three of the six PR #7 defects were exactly disagreements among these points.

**Facts duplicated rather than referenced:**

| Fact | Copies | Locations |
|---|---|---|
| 9-phase capability table with exit-gate outcomes | 2, verbatim | architecture spec; roadmap |
| Blocking-core / deferred-hardening scope split | 3 | spec; roadmap; Phase 0 plan |
| Temporal-claim non-expiry invariant | 4 | spec (twice); roadmap; plan (twice) |
| PR #3 duplicate-work collision narrative | 4 | roadmap; ADR-0002; ADR-0003; plan |
| ADR state | 2 per ADR | ADR header and index |
| Phase 0 acceptance criteria | 1 list + 2 partial restatements | spec; roadmap; plan |
| Acceptance evidence (fixture and test counts) | 2+ | roadmap gate-log row; PR records |

**Brittle reference forms observed:** counts of another file's list ("ten acceptance conditions" — actual 12); ordinals into another file's numbered sequence ("step 11"), silently broken by renumbering; frozen test/fixture counts in a durable log row; same-date log rows where row order is the only sequence signal; statuses (`In Progress`) that are definitionally facts about GitHub state, hand-copied into a file whose own header says GitHub owns live status.

## B. Mechanical vs judgment findings

Roughly half the findings were mechanically catchable with modest tooling; several more are *eliminated* — not just caught — by removing the duplication that produced them.

Mechanically catchable as-is: link resolution against the PR merge preview (three findings); count verification or a count ban (one); append-only chronological gate log (one); a deixis/transience lint banning "This PR", "currently", "today", working-tree assertions, and actor assignments in `docs/` (five findings, including one defect class fixed and re-committed by the same author in the same file the same day; the class was still live on the open Phase 0 plan at audit time — "the index was found stale at 108 nodes"); and scope/gate enforcement (PR #10 opened sixty-nine minutes after its author merged the roadmap text forbidding it, and the automated reviewer passed the code without consulting the gate).

Semi-mechanical, if the roadmap were data rather than prose: `Authorized` coexisting with a claim prohibition; a status contradicting the vocabulary defining it; "delivery level is empty" followed by delivery detail; an acceptance row naming one carried issue while prose names two.

Genuine judgment: which phase owns the "next" delivery slot; `Blocked` versus narrowing; where acceptance evidence belongs; ADR-0004's supersession enumeration; the plan omitting agent-behavior assets from the sync step — that last one judgment *caused by duplication*, since the plan re-derived the spec's scope and dropped one requirement in transit.

## C. Is the document architecture sound? No.

`docs/roadmap.md` is five documents sharing one file, with mutually hostile invariants: a status board (live state its own header assigns to GitHub), an append-only decision journal (the only content it legitimately owns), a delivery-detail cache (restated spec content whose two-level rule forces whole-section churn at every transition), near-constant policy text, and an evidence record (counts that rot). Every edit to one layer risks violating another layer's invariant — which is precisely the observed defect stream. A different factoring shrinks the invariant surface directly: the gate log becomes the roadmap's only owner-authored truth (append-only, PR references only, no evidence prose); the Status column carries decision-states only (`Authorized`/`Accepted`/`Deferred` — `In Progress`/`Blocked` deleted because GitHub already represents them); delivery detail is a link, never a restatement; the capability table exists in one file. That factoring eliminates the defect classes behind at least 10 of the 21 findings.

## D. Does the process create pressure toward these errors? Demonstrably.

Seven PRs in ~9 hours, with cross-references into unmerged branches: the Phase 0 plan was structurally required to reference an ADR that existed only on a sibling PR opened two minutes earlier — that broken-link finding was a certainty, not an accident. Two concurrent PRs managed a shared ADR file via a manual blob-hash-equality protocol — heroic coordination replacing the merge sequencing the process lacked, despite the spec's existing rule that roadmap gates and ADR indexes are serialized. Review-per-push amplified drift: each micro-edit was validated against the finding, never against the whole file's invariants, which is how a fixed defect class was re-committed same-day. Governance prose was written before the tooling that enforces it, and one PR walked through the paper gate unchallenged. ADR-0004 responded to the cost by reducing review of exactly the artifact class with the worst defect rate while the compensating mechanical checks remained deferred — cutting the detection instead of the defect source. The single GitHub identity for owner and both agents contributed materially to the PR #3 collision and makes attribution require prose archaeology.

## E. Highest-leverage structural changes

1. **Blocking docs CI**: link resolution on the merge preview, deixis/transience lint, count-of-referenced-list ban, gate-log append-only check. ~50 lines of Python. The spec's deferral of docs checks was the single most consequential error — during a docs-only construction phase, docs checks *are* the relevant CI. Would have prevented roughly ten findings.
2. **Refactor the roadmap per section C.** Would have prevented the status-contradiction family and the evidence-count rot.
3. **Enforce the existing serialization rule**: one open governance PR at a time; a PR may not reference a path absent from its own merge-base plus diff. Cost: one PR waits an hour. Cheaper than what happened.
4. **Build the minimal gate check before writing more governance** — a trivial check ("roadmap says implementation unclaimable → block non-docs diffs") would have stopped PR #10 at open. The spec's add-on-demonstrated-need posture is satisfied: the demonstration already happened.
5. **Single-copy rule** for the capability table and scope lists; a document that changes another's rules must quote-and-enumerate them, never restate its own summary.

**Closing observation:** the review process *worked* — nearly every defect was caught, and the dispositions are unusually honest. What failed is the economics: twenty-one findings were spent catching, by expensive adversarial reading, a defect class that a linter catches for free and a better document factoring never produces. The fix is not more care; it is fewer places where the same fact can disagree with itself, and a machine checking the places that remain.

---

## Disposition (by the audited author)

| Audit item | Action | Where |
|---|---|---|
| E1 + E4 — blocking docs CI and the gate check | Proposed as a new Step 0 of the Phase 0 plan, sequenced before further governance writing | PR #9 amendment; tracking issue |
| E2 + E5 — roadmap refactoring and single-copy rule | Tracking issue; executes after the Phase 0 plan is approved, as its own light-tier change | tracking issue |
| E3 — serialization enforcement | The one-governance-PR-at-a-time practice and the no-external-reference rule join the agent guide (plan step 10); the mechanical half joins the Step 0 checks | PR #9 amendment |
| Still-live deixis defect ("108 nodes") on the open plan | Fixed in the same PR #9 amendment | PR #9 |
| Critique of ADR-0004 as cutting detection | Accepted as fair. The light tier stands only in combination with the Step 0 checks; the ADR's own Consequences section already states the requirement the checks now perform | recorded here |
