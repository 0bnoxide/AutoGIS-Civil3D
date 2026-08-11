# Phase 3 Producer Adoption — Design

**Status:** Approved 2026-08-11 (owner). Governs the AutoGIS-Civil3D slice of
roadmap Phase 3 (authorized 2026-08-10, [gate-change log](../../roadmap.md)).
Producer internals are governed by the AutoGIS repository's own process; this
spec states only their contract-facing obligations.

## Problem

Phase 3's exit gate reads "AutoGIS emits conforming packages and passes
cross-repository compatibility checks." The producer (private
`0bnoxide/AutoGIS`) already writes LandXML 1.2 TIN surfaces through a
stdlib-only writer, but nothing there constructs
[contract v1](../../../contract/v1/README.md) packages, and nothing anywhere
proves producer output satisfies the contract. The repositories evolve
independently, so a one-time conformance demonstration decays immediately;
the gate needs a live, falsifiable check.

## Ownership split

The producer feature — manifest construction, ZIP assembly, conformance
alignment — is built in the AutoGIS repository under its own governance (its
ADR process, review bar, and CLI conventions). This repository owns the
cross-repository compatibility harness, the producer obligations below, and
the acceptance evidence. Dependency direction is unchanged from the
[handoff architecture](../../architecture-handoff.md): producer → contract →
validator; no code flows from this repository into the producer.

## Producer obligations

Stated as contract requirements, not implementation instructions:

1. A headless CLI command — exact name and flags are AutoGIS's choice,
   recorded in its ADR — that takes a LandXML TIN file plus explicit
   metadata and emits a contract-v1 package ZIP.
2. The emitted `surface.landxml` is produced by the production LandXML
   writer (the `write_landxml_surface` path), not passed through from the
   input: the packaged bytes are what real ArcGIS-sourced packages will
   contain.
3. **Never infer.** Units and horizontal EPSG resolve from the source
   through the producer's existing CRS machinery or the command fails
   loudly. Vertical datum comes only from explicit caller input and is
   otherwise declared `unknown`. No defaults, no normalization, matching
   the contract's own prohibition on consumers inferring alternatives.
4. No contract knowledge is duplicated: the manifest is built as plain
   data, with no vendored schema and no emission-time self-validation.
   Conformance is proven exclusively by this repository's validator.

## Compatibility harness

A CI job in this repository:

1. Checks out AutoGIS at a pinned commit using the read-only deploy key
   (issue #75).
2. Installs the producer's base package on the bare runner (five
   pure-Python dependencies; no ArcGIS).
3. Invokes the producer CLI on a LandXML file extracted from the existing
   [v1 fixture corpus](../../../fixtures/v1/README.md), passing explicit
   metadata including a known vertical datum.
4. Validates the emitted ZIP with the validator CLI built in the same job.
   The gate requirement is exit code 0 with **zero warnings** — the
   `unknown`-datum path stays legal for real use but is never the
   acceptance evidence.
5. Runs a negative control: the same validator invocation against one
   known-invalid fixture package must exit nonzero, proving the check can
   fail.

Advancing the AutoGIS pin is an ordinary pull request in this repository;
no automation, no scheduled bumps.

## Acceptance evidence

The bundle that flips Phase 3 to Accepted, collected on a Phase 3 gate
issue in this repository and cited by the eventual gate-change-log row
(the Phase 0 pattern):

- The compatibility job green on `main`, with the pinned AutoGIS commit
  recorded in the workflow.
- The negative control demonstrably failing-capable.
- The AutoGIS-side feature merged under its own ADR, with that ADR and PR
  linked from the gate issue.

## Exclusions

- No contract changes: v1 is frozen. Anything Phase 3 surfaces that seems
  to need a contract change is a new-version discussion, per the
  [contract versioning policy](../../../contract/v1/README.md).
- No Civil 3D claims: contract-valid is not import-tested; the live gate
  is Phase 5.
- No parking-lot items (multiple surfaces, DWG/DXF, signing, coordinate
  transformation).
- No vendored schema or emission-time self-validation in the producer.
- No artifact-publishing pipeline between the repositories.

## Known ceilings

- The check proves the pinned AutoGIS commit, not AutoGIS head; staleness
  is bounded by deliberate pin bumps.
- One fixture-derived input exercises one writer path. Breadth (more
  surface shapes, unit combinations) is added on contact, not
  speculatively.
- The job runs on the CI runner platform only; producer behavior on other
  platforms is out of scope here.
