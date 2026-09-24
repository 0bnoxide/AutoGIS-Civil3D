# ADR-0011: Place local handoff MCP validation in Phase 4

**Status:** Accepted 2026-09-24 by the owner's instructions to proceed with the recommended implementation and “Extend Phase 4 for M1.”

## Context

The accepted Phase 2 validator already checks contract ZIPs without Autodesk. The [MCP evaluation](../research/civil3d-2026-mcp-evaluation.md) recommended exposing that library through one local, read-only tool before considering native drawing access. This is a new product interface, not maintenance of the accepted validator or part of the existing New Proposal approval. The [roadmap](../roadmap.md) therefore requires an owner phase decision before implementation.

## Decision

Add M1, the Autodesk-free handoff validation MCP facade, as a **narrow Phase 4 enabling slice**. It has one stdio tool over the existing validator and no Civil 3D plugin, drawing access, import claim, or write capability. The owner-approved [design](../superpowers/specs/2026-09-24-local-handoff-mcp-design.md) and [implementation plan](../superpowers/plans/2026-09-24-local-handoff-mcp-implementation.md) govern its scope.

Phase 4 remains Authorized with its existing New Proposal exit gate. Phase 5 remains closed; this decision grants no native inspection or import authority. The roadmap records the owner decision and reserves the M1 source and test paths until the documentation gate is removed in a separate documentation-only change. Product implementation starts only after that removal merges.

## Alternatives considered

- Wait for Phase 5: it would fit later package inspection, but the owner chose this local preflight as Phase 4 enablement. The tool itself never inspects a Civil 3D drawing or imports a package.
- Install or fork a public Civil 3D bridge: the [evaluation](../research/civil3d-2026-mcp-evaluation.md) found no independently qualified match for this repository's 2026 host and approval boundary.
- Treat M1 as Phase 2 maintenance: rejected because an MCP interface is a new capability, even though it reuses Phase 2 code.

## Consequences

The repository gains an agent-facing package check without expanding the native adapter. Its result can establish contract validity only. Release configuration must choose an owner-controlled staging root; tests and a protocol trace must prove path and output limits. Future host access and writes require their own approved workflow and gate decisions.
