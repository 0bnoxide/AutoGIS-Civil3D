# Port Backlog Plan: Structural & Functional Advantages

**Date:** 2026-08-07

**Status:** Accepted — ports may be claimed. P2 additionally requires explicit owner sign-off before it is claimed (see its Risk note).

**Source:** [Issue #12](https://github.com/0bnoxide/AutoGIS-Civil3D/issues/12) — a ranked inventory of AutoGIS structural machinery. Each item there is "a candidate, not a commitment." This plan assesses each on the advantage it brings *here* and selects what to pull over. Patching an artifact on the way in is expected, not a reason to skip it.

**Governing decisions:** [ADR-0001](../../adr/0001-handoff-contract-ownership.md) (contract owns a product-neutral validator), [ADR-0004](../../adr/0004-one-adversarial-review-proportioned-to-risk.md) (one adversarial review, proportioned to risk).

## The filter

Port a candidate when both hold:

1. **Advantage here** — it adds a structural or functional capability this repo lacks, or guards an invariant currently unguarded. A capability already fully covered elsewhere in this repo is not an advantage.
2. **Portable design** — the *idea* is sound. A defect in AutoGIS's implementation (a CI-unwired self-check, drifted hook twins, a hardcoded path) is a **patch work-item on the way in**, not a disqualifier — it is listed in that port's steps.

Excluded only when the capability is already covered here (restatement / redundant with stronger enforcement) or brings no advantage worth its upkeep (YAGNI).

## Verdict summary

| # | Candidate | Advantage here | Disposition |
|---|---|---|---|
| 2c | Forbidden-dependency boundary test | Guards an ADR-0001 invariant that is unguarded today | **PORT — P1** |
| 1 | Five-probe review framework | Defines "substantial" (ADR-0004 leaves it open) | **PORT — P2** |
| 3 | PR-review monitor | Polls 3 comment surfaces, dedups by id, no silent fail | **PORT — P3** (patch: wire self-check to CI) |
| 6 | Post-edit / post-push feedback hooks | Faster edit→failure loop; PR URL after push | **PORT — P4** (patch: shared neutral script) |
| 2a | ADR-numbering invariant test | Catches a hand-edit the allocator can't | Port — low (fold into `docs_checks.py`) |
| 5b | `/pr-doctor` | Diagnoses a stuck PR from checks/reviews/logs | Port — low (prompt skill) |
| 4 | Graph-nav agent + `/graph` | Marginal — capability reachable via MCP already | Follow-up |
| 2d | Shipped examples executed by CI | No shipped examples beyond fixtures today | Follow-up |
| 2b | Frontmatter via real YAML loader | None — conflicts with `sync.py` design | Excluded |
| 5a | `/ship`, `/new-issue` | Redundant with stronger enforcement / a convention | Excluded |
| 7 | Documentation durability contract | Substance already distributed here | Excluded |
| 8 | Daily agent-decision logs | Convention; ADRs + roadmap already carry decisions | Excluded |

---

## P1 — Forbidden-dependency boundary test *(port)*

**Advantage.** [ADR-0001](../../adr/0001-handoff-contract-ownership.md) states as a Consequence: *"ArcGIS and Autodesk references cannot enter the core validation library."* The boundary is clean today — `AutoGIS.Civil3D.Handoff.csproj` references only `JsonSchema.Net` and `SharpZipLib` — but **nothing fails the build if that changes.** A stray Autodesk `PackageReference` or transitive Esri assembly would silently dissolve the seam whose entire purpose (ADR-0001) is "tested without either desktop application."

**Design ported.** AutoGIS proves a banned import never loads by walking modules in a fresh subprocess. The .NET analogue is a reference-closure assertion in `AutoGIS.Civil3D.Handoff.Tests`: inspect the Handoff assembly's referenced-assembly closure and assert no name matches a banned prefix set (`Autodesk`, `Aecc`, `acdbmgd`, `acmgd`, `ArcGIS`, `ESRI`). Runs in the existing `dotnet test` CI job — no new job, no new dependency.

**Patch on the way in.** `GetReferencedAssemblies()` drops declared-but-unused references. If guarding the *declaration* matters, add a second cheap scan of the restored `project.assets.json` (or `dotnet list package`) against the same set. Start with the reflection test; add the assets scan only if a declared-unused reference is a real concern.

**Acceptance.** Passes on today's clean boundary; proven red by temporarily flagging an assembly the closure actually references (a declared-but-unused package is dropped by the compiler and stays out of the reflection test's reach — that gap is the assets-scan deferral above, not a hole in the red proof) before landing green; wired into the `test` CI job so a future violation blocks merge.

**Risk.** Low — additive test, no production-code change.

---

## P2 — Five-probe review framework *(port)*

**Advantage.** [ADR-0004](../../adr/0004-one-adversarial-review-proportioned-to-risk.md) sets the merge bar at "one *substantial* adversarial review" but never defines *substantial*. `tools/agent-assets/agents/pr-reviewer.md` encodes review rules but no structured probe pass. The five probes supply the missing definition.

**Design ported.** `BOUNDARY_SHAPE`, `CONTRACT_REACHABILITY`, `IDENTITY_PROVENANCE`, `SIDE_EFFECT_SAFETY`, `ENVIRONMENT_SEAM` — each classified PASS, FAIL, or N/A with evidence, plus the teeth rule *"a green suite is not evidence for a probe the suite bypasses."* Fold into the canonical `tools/agent-assets/agents/pr-reviewer.md` (edit the canonical source, then `python tools/agent-assets/sync.py` — never the rendered `.claude`/`.codex` copies), framed as the content of a full-tier review; ADR-0004 keeps the *when*, the probes supply the *what*, joined by a single pointer, not a duplicated tier table.

**Patch on the way in.** Do not port AutoGIS's `pr-review-failure-mode-audit.md` wholesale — its 267-comment derivation is AutoGIS history. Adapt the probe definitions to this repo's vocabulary (handoff manifest, issue codes, validator seam). Add an invariant test that parses `pr-reviewer.md` and asserts the five probe IDs are present, so a prompt cleanup cannot silently delete the framework — using the existing stdlib `parse_frontmatter`, **no YAML dependency** (this is the salvageable half of candidate #2b).

**Acceptance.** `pr-reviewer.md` requires each probe classified with evidence; `sync --check` / `test_sync.py` stay green after the canonical edit + resync; the probe-ID invariant test fails if any of the five IDs is removed.

**Risk.** Touches governance (defines an ADR-0004 term) — get explicit owner sign-off before claiming.

---

## P3 — PR-review monitor *(port; patch defect)*

**Advantage.** The hand-rolled monitors used this session polled fewer GitHub surfaces and echoed our own comments back as events. AutoGIS's `watch-pr-reviews.sh` fixes exactly that: it polls all three distinct surfaces — `pulls/{n}/reviews`, `pulls/{n}/comments`, `issues/{n}/comments` — paginated; dedups by `TYPE id` rather than display line (inline `path:line` shifts on every push); and emits `POLL-FAIL` instead of going silent when `gh` errors. That is a concrete functional advantage over what we keep re-improvising.

**Patch on the way in — this is the reason it was almost skipped.** AutoGIS ships a stubbed-`gh` self-check (`test-watch-pr-reviews.sh`) that **no CI job or hook runs** — a real test left dead. Porting the script *and* wiring its self-check into the `coordination` CI job (alongside the existing Python suites) is the whole point: pull the good design, fix the defect that made it unverified. A monitor without an enforced self-check is not worth porting.

**Scope note.** Issue #11 (the original trigger) closed as owner-action-outside-repo; this does not unblock #11. It exists because sessions keep hand-rolling weaker pollers. The dedup key must ignore our own bot/author identity so the monitor does not echo our comments (the specific bug observed this session).

**Acceptance.** The stubbed-`gh` self-check runs in CI and fails on a regression (missing surface, silent failure, self-echo); a live smoke run against an open PR reports each surface once.

**Risk.** Low — a read-only reporting script plus a CI wire-up; no repo-state mutation.

---

## P4 — Post-edit / post-push feedback hooks *(port; patch defect)*

**Advantage.** The repo configures only `SessionStart` and `PreToolUse` — there is no `PostToolUse` feedback. Two loops are worth tightening: after a source edit, run the touched project's tests and feed **only failures** back as context; after `git push`, surface the open PR URL.

**Patch on the way in.** AutoGIS's hooks are the flagged defect set — drifted `.claude`/`.codex` twins, hardcoded absolute paths. Do not inherit that. Land the behavior on the pattern PR #10 already proved here: **one shared script on a neutral path**, its root derived per session from `git rev-parse --show-toplevel` (the session's worktree — so the touched-project tests and the pushed branch's PR resolve in that session, not the primary checkout that `--git-common-dir` would target), wired identically for both harnesses. That converts the defect into a solved pattern.

**Ponytail ceiling.** The post-edit test run can be noisy and slow — the .NET build dominates. Scope it: touched-project only, failures-only output, and keep the slow .NET leg opt-in (fast Python suites run inline; `dotnet test` gated behind a marker or only on `.cs` edits). The post-push PR-URL surface is trivial and lands regardless.
`# ponytail: touched-project scope only; widen to full-suite feedback if a cross-project break slips through.`

**Acceptance.** Editing a file with a failing test surfaces that failure and nothing else; a passing edit is silent; a push prints the PR URL; both harnesses invoke one script, no path literals.

**Risk.** Low–medium — hook noise/latency is the main hazard, bounded by the scope note above.

---

## Port — low priority

- **2a — ADR-numbering invariant test.** The allocator (`claim --kind adr`) prevents session collisions (the `0005` index gap is a deliberate consumed allocation, not a clash), but nothing catches a *hand-edited* duplicate prefix or an `H1 ≠ filename` mismatch. A few lines in `tools/checks/docs_checks.py`, which already lints docs. Cheap; land when convenient.
- **5b — `/pr-doctor`.** A prompt skill that diagnoses a stuck PR from comments, reviews, checks, and failed-run logs. Genuinely new and useful when a PR stalls; unenforceable convenience. Port if the manual version recurs.

## Follow-up — recorded, not built now

- **4 — Graph-nav agent + `/graph`.** The codebase-memory MCP is connected and `docs/agent-guide.md` rule 6 already routes navigation through it, so the capability exists. The only delta is an index-health preflight before trusting the index — a thin advantage. Revisit if stale-index trust causes a real miss.
- **2d — shipped examples executed by CI.** CI already runs diagnostic-package validators; there are no shipped examples beyond fixtures today. Revisit when starter templates ship.

## Excluded — with reasons

- **2b — Frontmatter via real YAML loader.** Conflicts with `sync.py`'s deliberate stdlib-only `parse_frontmatter` (documented S8786 rationale). The one useful half — pinning probe IDs — is folded into **P2** with no new dependency.
- **5a — `/ship`, `/new-issue`.** `/ship`'s refuse-push-from-`main` is redundant with strictly stronger enforcement (`.githooks/pre-push` + PreToolUse). `/new-issue` restates the always-open-an-issue convention already in the agent guide.
- **7 — Documentation durability contract.** The substance already lives here, distributed: the `agent-guide.md` sources-of-truth table + `docs_checks.py`'s `LIVING_DOCS` set (durable-authoritative vs. dated-record, enforced). A fourth doc classifying docs restates authority already encoded.
- **8 — Daily agent-decision logs.** A logging convention with no verification; ADRs and the roadmap already carry durable decisions with revisit triggers. YAGNI until an auditable per-day trail is a demonstrated need.

## Sequencing

Independent PRs, no cross-dependencies. Suggested order by risk/value: **P1** (guards a live invariant, lowest risk) → **P3** (removes recurring re-improvisation) → **P4** (dev-loop tightening) → **P2** (governance, needs sign-off). The two low-priority items fold in opportunistically.
