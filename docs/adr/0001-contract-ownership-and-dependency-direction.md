# ADR-0001: AutoGIS-Civil3D owns the handoff contract and its dependency direction

**State:** Accepted (owner acceptance of the collaboration architecture, PR #1, 2026-08-03; merged `59cf551`)

**Date:** 2026-08-02

## Context

AutoGIS produces surface data on the ArcGIS side; this repository will later host the Autodesk/Civil 3D consumer. A handoff seam between them needs one owner, or the contract drifts into whichever codebase touched it last.

## Decision

AutoGIS-Civil3D owns handoff contract v1: the ZIP package shape, `handoff.json` JSON Schema, LandXML surface rules, safety limits, and issue-code policy. The dependency direction is one-way: AutoGIS adopts the contract as a producer (Phase 3); the contract and its validator never depend on ArcGIS, ArcPy, Autodesk, or Civil 3D assemblies. The validator and CLI are pure .NET 8 and must restore, build, and test without any Autodesk SDK present.

## Alternatives

Contract owned by AutoGIS (rejected: couples the seam to ArcPy tooling and inverts the consumer's authority over what it can import); a third shared-contract repository (rejected: governance overhead for two repositories and one seam).

## Consequences

Contract changes are made here, versioned here, and consumed by AutoGIS through released contract versions. Cross-repository compatibility checks are the Phase 3 exit gate. Contract schemas are serialized artifacts under the collaboration model — never edited in parallel slices.
