# Architecture Decision Records

ADRs record decisions, context, alternatives, and consequences. They do not carry live task status. States: `Proposed`, `Accepted`, `Deprecated`, `Superseded`.

| ADR | Title | State |
|---|---|---|
| [0001](0001-handoff-contract-ownership.md) | AutoGIS-Civil3D owns the handoff contract | Accepted |
| [0002](0002-agent-collaboration-and-main-protection.md) | Claude/Codex collaboration, neutral agent structure, and local-first `main` protection | Accepted |
| [0003](0003-contract-slice-precedes-phase-0.md) | Execute the Phase 1–2 contract slice before Phase 0 | Accepted |
| [0004](0004-one-adversarial-review-proportioned-to-risk.md) | One adversarial review before merge, proportioned to risk | Accepted |

ADR numbers are allocated, never guessed: take one with `coordination.py claim --session <id> --kind adr`, which allocates the next unused number atomically under the registry lock. A number is consumed on allocation and never reissued, so an unused allocation leaves a gap in this index — 0005 is such a gap, consumed by an allocation probe.
