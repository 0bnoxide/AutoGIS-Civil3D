# Architecture Decision Records

ADRs record decisions, context, alternatives, and consequences. They do not carry live task status. States: `Proposed`, `Accepted`, `Deprecated`, `Superseded`.

| ADR | Title | State |
|---|---|---|
| [0001](0001-handoff-contract-ownership.md) | AutoGIS-Civil3D owns the handoff contract | Accepted |
| [0002](0002-agent-collaboration-and-main-protection.md) | Claude/Codex collaboration, neutral agent structure, and local-first `main` protection | Accepted |
| [0003](0003-contract-slice-precedes-phase-0.md) | Execute the Phase 1–2 contract slice before Phase 0 | Accepted |
| [0004](0004-one-adversarial-review-proportioned-to-risk.md) | One adversarial review before merge, proportioned to risk | Accepted |
| 0005 | Permanently consumed by allocation probe; no ADR | — |
| [0006](0006-civil-production-accelerator.md) | The product is a Civil Production Accelerator, `New Proposal` first | Accepted |
| [0007](0007-civil3d-2025-reference-sourcing.md) | Compile the adapter against pinned Civil 3D 2025 reference packages | Accepted |
| [0008](0008-civil3d-2026-development-target.md) | Develop and qualify New Proposal on Civil 3D 2026 | Accepted |
| [0009](0009-hosted-ci-manual-integration.md) | Build the 2026 preview in hosted CI and qualify it manually | Accepted |

ADR numbers are allocated, never guessed: take one with `coordination.py claim --session <id> --kind adr`, which allocates the next unused number atomically under the registry lock. A number is consumed on allocation and never reissued. Record unused allocations as plain four-digit rows in this table before retiring their local registry so fresh clones preserve those consumed gaps. The allocator uses the highest number from this table, existing ADR filenames, and local reservations; `doctor` reports when the local registry is behind the index. Keep this index to one ADR table with linked ADR numbers or plain consumed numbers in its first column.
