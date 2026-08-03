# ADR-0001: AutoGIS-Civil3D owns the handoff contract

**Status:** Accepted

## Context

AutoGIS produces surface geometry, while Civil 3D consumption needs a stable boundary that can be tested without either desktop application.

## Decision

This repository owns the versioned schema, semantic rules, issue codes, validator, CLI, and conformance fixtures. AutoGIS will adopt the contract as a producer. Autodesk-specific code will live in a future adapter that depends on the pure validator.

## Consequences

- Contract tests run with .NET 8 only.
- Producer and consumer changes are checked against the same golden ZIPs.
- ArcGIS and Autodesk references cannot enter the core validation library.
- Live Civil 3D import remains a separate release gate.
