# ADR-0001: AutoGIS-Civil3D owns the handoff contract

**Status:** Accepted

## Context

AutoGIS produces surface geometry, while Civil 3D consumption needs a stable boundary that can be tested without either desktop application.

## Decision

This repository owns the versioned schema, semantic rules, issue codes, validator, CLI, and conformance fixtures. AutoGIS will adopt the contract as a producer. Autodesk-specific code will live in a future adapter that depends on the pure validator.

## Alternatives

- AutoGIS owns the contract. Rejected: it inverts the dependency direction, couples the seam to ArcPy tooling, and gives the producer authority over what the consumer must accept.
- A third shared-contract repository. Rejected: two repositories of governance overhead for one seam, with no owner closer to the Civil 3D constraints.
- No intermediate contract; integrate AutoGIS directly against the Civil 3D API. Rejected: nothing can then be tested without both desktop applications present, which is the constraint this boundary exists to remove.

## Consequences

- Contract tests run with .NET 8 only.
- Producer and consumer changes are checked against the same golden ZIPs.
- ArcGIS and Autodesk references cannot enter the core validation library.
- Live Civil 3D import remains a separate release gate.
