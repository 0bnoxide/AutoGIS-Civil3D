# ADR-0006: The product is a Civil Production Accelerator, `New Proposal` first

**State:** Accepted (owner decision, 2026-09-04, recorded in the Civil
Production Accelerator implementation handoff quoted in the gate-change-log
row this ADR accompanies)

**Date:** 2026-09-04

## Context

Phases 0–3 delivered a language-neutral handoff contract, a pure .NET
validator and CLI, and producer adoption. Every document that states a
direction — the repository README, the
[handoff architecture](../architecture-handoff.md), and the roadmap's
Phase 4–8 sequence — describes the future as contract-first handoff tooling
whose destination is a Civil 3D adapter that imports a validated package.

That sequence never reaches the work the owner needs automated. The
repetitive Civil 3D production tasks — project setup, model checking,
repetitive design production, deliverable preparation — are not import
problems, and none of them consumes a LandXML bundle. Under the sequence as
recorded, the first capability a user could run sits behind Phase 5 at the
earliest, behind an adapter whose approved design deliberately
[contains no commands](../superpowers/specs/2026-09-04-phase-4-adapter-foundation-design.md),
and behind an import path that no user workflow has asked for.

The foundation itself is sound: fail-closed staged validation, immutable
result models, stable issue codes, a golden fixture corpus, an enforced
Autodesk-free reference boundary, deterministic Windows CI, and a
diagnostic kit that has proven command registration and Civil 3D API access
on a live workstation. What drifted is what that foundation is pointed at.

The owner has restated the product.

## Decision

1. **Product definition.** AutoGIS-Civil3D is a Civil Production
   Accelerator that uses deterministic, previewable, auditable automations
   to reduce project setup, model checking, repetitive design production,
   and deliverable preparation time. User-facing Civil 3D workflows are the
   product; the handoff contract and validator are supported infrastructure
   beneath them, not the headline.

2. **Phase 4 becomes the Civil production foundation**, and its first slice
   is `New Proposal`, a native Civil 3D command that scaffolds a proposal
   project. Phases 5 and 6 keep their numbers and their read-only-before-
   write ordering; their capabilities are restated in production terms in
   [the roadmap](../roadmap.md). Phases 0–3 and their accepted evidence are
   untouched, and no phase is renumbered.

3. **Core/Adapter boundary.** `src/AutoGIS.Civil3D.Proposal/` is an
   Autodesk-free core that owns inputs, the standards manifest, naming and
   path rules, deterministic planning, verification contracts, and receipt
   contracts; it touches neither the filesystem nor an Autodesk API.
   `src/AutoGIS.Civil3D.Adapter/` remains the one product project that may
   reference Autodesk, and owns command registration, UI, execution,
   native verification, and receipt presentation. The core's boundary is
   enforced by a reference test of the kind that already guards the
   validator.

4. **Civil-local ownership.** The Civil-local project configuration,
   standards manifest, source register, execution plan, and run receipts
   are owned by this repository and live with the proposal. AutoGIS-owned
   analytical and GIS project state is referenced, never duplicated here.

5. **Preserved invariants.** Every production workflow fails closed on
   ambiguity, previews its complete plan before writing, requires explicit
   approval, verifies its outputs against the approved plan, and emits an
   auditable receipt. Contract v1 stays frozen; validator, CLI, fixture,
   and diagnostic behavior is unchanged.

6. **Native qualification is part of done.** No workflow may be described
   as supported on a Civil 3D release without a qualification run in that
   release using the owner's real templates. Green CI is not sufficient
   evidence.

The requirements this decision approves are specified in the
[Civil Production Accelerator design](../superpowers/specs/2026-09-04-civil-production-accelerator-design.md).

## Alternatives

**Keep the contract-first sequence and append production workflows after
Phase 8.** Rejected: it defers every user-visible benefit behind an entire
adapter, import, automation, and packaging program, and the owner's stated
need is the reason the repository exists.

**Adopt the incoming handoff's own Phase 1/2/3 track numbering.** Rejected:
this repository's Phases 1–3 are accepted history with recorded evidence.
Renumbering would rewrite accepted rows and every cross-reference to them,
for no capability gain. Track names map onto existing phase numbers without
moving a single accepted row.

**Ship a standalone launcher instead of a native command.** Rejected for
release 1: the workflow needs the host's document state and template
resolution, and the preserved diagnostic kit already proved command
registration and API access inside Civil 3D.

**Build the production foundation on the validator seam.** Rejected:
`New Proposal` consumes no LandXML bundle, so `BundleValidator` is not its
application seam, and forcing it through one would couple unrelated
lifecycles.

## Consequences

- The approved Phase 4 adapter-foundation design is partially superseded
  (below). The reserved implementation paths in the roadmap marker grow by
  the core project and its tests; nothing is un-reserved.
- Company standards become data. A versioned standards manifest owns folder
  structure, template paths, filenames, title-block mappings, page setups,
  and placeholder geometry. Proprietary templates never enter this
  repository; tests use synthetic ones.
- Details the owner has not yet supplied — folder tree, template paths,
  sheet numbering, title-block keys, placeholder geometry and semantics —
  are recorded as open inputs in the design and asked one at a time. They
  are not fabricated, and they do not block Autodesk-free planning work.
- Import of a contract-valid package remains a capability under Phase 5,
  sequenced behind the production foundation rather than driving it.
- Adding capability to an accepted phase's deliverables still needs its own
  gate; this ADR authorizes no phase beyond the Phase 4 already authorized
  on 2026-08-13.

This ADR supersedes two decisions in the
[Phase 4 adapter-foundation design](../superpowers/specs/2026-09-04-phase-4-adapter-foundation-design.md),
and nothing else:

1. Its **adapter seam** decision — widening the validator's public surface
   with manifest facts and verified-byte access. That surface exists only
   to serve import, which is no longer the next slice; it is deferred to
   the phase that implements import, and the validator's public surface
   stays as accepted in Phase 2.
2. Its **scope bound** that the Phase 4 adapter contains no commands, no
   drawing access, and no extension-application entry point, and that
   everything running inside Civil 3D belongs to Phase 5. Phase 4 now ships
   a running command.

Retained from that design, unchanged and load-bearing for the adapter:
its project and targeting decisions, its reference-assembly sourcing
decision and series check, its exclusions on contract and diagnostics, and
its known ceilings.
