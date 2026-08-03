# AutoGIS-Civil3D LandXML Handoff Contract Design

**Date:** 2026-08-02

**Status:** Approved for implementation planning

**Scope:** Steps 1-4: repository baseline, versioned handoff contract, read-only validator, and sanitized golden fixtures

## Context

AutoGIS can export the authoritative vertices and triangular faces of an ArcGIS TIN as LandXML. The receiving side needs a predictable, auditable boundary before any Autodesk API or interactive Civil 3D import is involved. A bare LandXML file does not provide enough packaging, integrity, provenance, or policy information to serve as that boundary by itself.

`AutoGIS-Civil3D` will own a language-neutral, versioned handoff contract. AutoGIS will later adopt the contract as a producer. The first implementation slice validates a single triangulated surface package without requiring ArcGIS Pro, Civil 3D, or Autodesk SDK assemblies.

## Goals

- Establish the new repository's baseline documentation and preserve the repaired diagnostic kit with clear provenance.
- Define one canonical version-1 ZIP package for one triangulated LandXML surface.
- Provide a C#/.NET 8 validation library whose public interface hides ZIP, JSON, and XML parsing details.
- Provide a thin command-line interface over the library.
- Reject malformed, inconsistent, or unsafe packages without extracting them.
- Represent an unknown vertical datum as a prominent warning while still requiring a vertical unit and positive-up direction.
- Prove every rule with small, synthetic, sanitized ZIP fixtures.
- Preserve live Civil 3D import as a separate functional-validation gate.

## Non-goals

This slice does not:

- Change AutoGIS or add its producer integration.
- Reference AutoCAD ObjectARX or Civil 3D managed assemblies.
- Automate a Civil 3D import.
- Claim that contract validation proves Civil 3D compatibility.
- Carry DWG, DXF, alignments, profiles, corridors, or multiple surfaces.
- Transform coordinates, elevations, CRS, units, or vertical datums.
- Support package signing, encryption, or arbitrary attachments.

## Ownership and dependency direction

`AutoGIS-Civil3D` is the canonical owner of the handoff schema, semantic rules, fixtures, and validation issue codes. The dependency direction is:

```text
AutoGIS producer (later)
          |
          v
versioned ZIP contract
          |
          v
pure .NET 8 validator <- future Autodesk/Civil 3D adapter
```

The future Autodesk adapter may reference the pure handoff library. The handoff library must never reference Autodesk or ArcGIS assemblies. This keeps contract tests runnable on an ordinary .NET 8 machine and prevents application-specific APIs from leaking into the exchange boundary.

## Repository shape

The implementation plan will establish this structure:

```text
contract/v1/handoff-manifest.schema.json
src/AutoGIS.Civil3D.Handoff/
src/AutoGIS.Civil3D.Handoff.Cli/
tests/AutoGIS.Civil3D.Handoff.Tests/
fixtures/v1/valid/
fixtures/v1/invalid/
diagnostics/AutoGIS.Civil3D.Diagnostics/
artifacts/diagnostics/original/
artifacts/diagnostics/current/
docs/architecture-handoff.md
docs/adr/0001-handoff-contract-ownership.md
docs/superpowers/specs/
```

The repaired diagnostic source belongs under `diagnostics/`. Version 0.1.0 remains historical evidence under `artifacts/diagnostics/original/`; version 0.1.1 is the current audited ZIP under `artifacts/diagnostics/current/`. Documentation will record both SHA-256 hashes and label 0.1.0 as superseded. The approved design specification is the first governance commit; organizing the diagnostics and establishing the remaining baseline is the first implementation commit.

## SDK strategy

The first implementation prerequisite is the Microsoft .NET 8 SDK. Setup is complete only when `dotnet --list-sdks` reports an 8.x SDK and a minimal restore/build/test cycle succeeds.

Both the library and CLI target `net8.0`. Autodesk AutoCAD 2025 ObjectARX SDK references are deferred until a separate Civil 3D adapter is designed. Neither Autodesk SDK files nor a Civil 3D installation are required for this slice.

## Canonical version-1 package

The transport is one ZIP file containing exactly two regular files at its root:

```text
handoff.json
surface.landxml
```

Names are exact and case-sensitive. Directories, additional entries, alternate names, case-colliding entries, encrypted entries, and symbolic-link entries are invalid. Version 1 permits ZIP `store` and `deflate` compression only.

The validator does not extract the archive. It checks declared entry sizes before reading, counts bytes while streaming, and applies these default limits:

- `handoff.json`: 1 MiB uncompressed.
- `surface.landxml`: 2 GiB uncompressed.
- Per-entry compression ratio: at most 100:1.
- Entry count: exactly two.

The limits are library policy constants in version 1. Changing them is a deliberate contract-policy change with corresponding fixtures and release notes.

## Manifest contract

`contract/v1/handoff-manifest.schema.json` is the normative structural definition. It uses JSON Schema 2020-12, requires UTF-8 JSON, and sets `additionalProperties: false` for every object. Version 1 requires the following logical structure:

```json
{
  "contract_version": "1.0",
  "package_id": "9a8ff271-b0d8-46db-809d-a6f72954af20",
  "created_utc": "2026-08-02T00:00:00Z",
  "producer": {
    "name": "AutoGIS",
    "version": "1.0.0",
    "source_commit": "optional-revision"
  },
  "surface": {
    "filename": "surface.landxml",
    "sha256": "64-lowercase-hexadecimal-characters",
    "landxml_version": "1.2",
    "name": "Existing Ground",
    "point_count": 4,
    "face_count": 2
  },
  "coordinate_reference": {
    "horizontal": {
      "kind": "projected",
      "authority": "EPSG",
      "code": 2256,
      "unit": "us_survey_foot"
    },
    "vertical": {
      "unit": "us_survey_foot",
      "direction": "positive_up",
      "datum": {
        "status": "known",
        "authority": "EPSG",
        "code": 5703,
        "name": "NAVD88 height"
      }
    }
  }
}
```

Rules not fully expressible in JSON Schema are normative semantic rules in the validator:

- `contract_version` is exactly `1.0`.
- `package_id` is an RFC 4122 UUID string.
- `created_utc` is an ISO 8601 timestamp normalized to UTC with a trailing `Z`.
- Producer name and version are required. `source_commit` is optional. Producer data must not contain usernames or absolute machine paths.
- `surface.filename` is exactly `surface.landxml`.
- `surface.sha256` is the lowercase SHA-256 digest of the raw uncompressed `surface.landxml` bytes.
- Version 1 supports LandXML 1.2 only.
- Surface name is nonempty after trimming; point and face counts are positive integers.
- Horizontal CRS kind is exactly `projected`, authority is exactly `EPSG`, and code is a positive integer. The same declaration must appear in the LandXML metadata.
- Horizontal and vertical units are one of `metre`, `international_foot`, or `us_survey_foot` and must agree with the LandXML metadata.
- Vertical direction is exactly `positive_up`.

Vertical datum uses one of two shapes:

- `known`: authority, positive integer code, and nonempty name are required and must agree with LandXML metadata.
- `unknown`: authority and code are prohibited; an optional nonempty note may explain the uncertainty. The package is structurally valid but receives a human-review warning. LandXML must not assert a conflicting known datum.

The manifest deliberately carries no source dataset path, Windows username, workstation name, or customer identifier.

## Library interface

`AutoGIS.Civil3D.Handoff` is a deep module with a small public entry point conceptually equivalent to:

```text
ValidateBundle(path, optional policy) -> ValidationReport
```

Callers receive a report rather than needing to understand ZIP entries, JSON Schema, XML namespaces, hashes, or triangle topology. `ValidationReport` contains:

- Overall status: `Valid`, `ValidWithWarnings`, or `Invalid`.
- An ordered list of issues.
- For each issue: stable code, severity, concise message, and location when available.
- Verified package metadata when validation reached that stage.

Malformed or hostile user data produces report issues rather than escaping as parsing exceptions. Programmer defects and unrecoverable runtime failures may throw internally; the CLI catches them and reports an operational failure without a stack trace unless diagnostic verbosity is explicitly requested.

Stable issue-code families are reserved by validation layer:

- `ZIPxxx`: container and streaming safety.
- `MANxxx`: manifest and schema.
- `INTxxx`: checksums and package integrity.
- `XMLxxx`: LandXML structure and topology.
- `XCKxxx`: manifest-to-LandXML cross-checks.
- `WRNxxx`: accepted conditions requiring human review.

Specific codes become compatibility surface once released and therefore require regression tests before alteration.

## Validation flow

Validation is deterministic, read-only, and stops expensive downstream work when an earlier trust boundary fails:

1. Open the ZIP and enforce entry count, exact names, regular-file status, compression methods, declared sizes, and ratio limits.
2. Stream and parse `handoff.json`; enforce JSON Schema and semantic manifest rules.
3. Stream `surface.landxml`, count actual bytes, and calculate SHA-256.
4. Compare the calculated digest with the manifest before accepting XML semantics.
5. Parse LandXML with DTD processing prohibited and external resource resolution disabled.
6. Enforce exactly one LandXML 1.2 `Surface` with one TIN definition, points, and faces.
7. Validate every point has a unique identifier and exactly three finite numeric coordinates.
8. Validate every face has exactly three distinct point references and every reference resolves.
9. Reject triangles with coincident vertices or zero/near-zero projected horizontal area. Near-zero means the absolute 2D cross product is at most `1e-12` times the largest squared edge length.
10. Cross-check surface name, LandXML version, point count, face count, EPSG declaration, units, vertical direction, and vertical-datum declaration against the manifest.
11. Return an ordered report. Errors determine `Invalid`; no errors plus one or more warnings determine `ValidWithWarnings`; otherwise the result is `Valid`.

Unknown vertical datum emits a prominent warning explaining that elevation alignment must be confirmed before use. The validator does not transform or guess the datum.

## CLI behavior

The CLI is a thin renderer over the library. It accepts one ZIP path and produces a concise text report. Machine-readable JSON rendering may be included only as a direct representation of `ValidationReport`; it must not implement separate validation logic.

Exit codes are stable:

- `0`: valid without warnings.
- `1`: invalid package.
- `2`: valid package with warnings requiring human review.
- `3`: invocation, filesystem, or unexpected operational failure.

The summary must say that contract-valid is not equivalent to Civil 3D import-tested.

## Golden fixtures

All fixtures are synthetic and contain no customer geometry, project names, usernames, absolute paths, or workstation data. Checked-in goldens are actual `.zip` files so tests exercise the canonical transport. Fixture creation is deterministic: fixed entry order, fixed timestamps, UTF-8 text, and stable compression settings.

The minimum valid set is:

- Known vertical datum, expected `Valid` and exit code 0.
- Unknown vertical datum, expected `ValidWithWarnings`, one stable warning code, and exit code 2.

Each invalid fixture changes one condition from a valid baseline and asserts one primary stable issue code. The minimum invalid set covers:

- Missing, extra, unsafe-path, case-colliding, encrypted, and unsupported-compression ZIP entries.
- Oversized entries and excessive compression ratio.
- Missing manifest fields, unknown properties, invalid contract version, and invalid timestamp.
- Bad checksum and wrong filename.
- Malformed XML, forbidden DTD, wrong LandXML version, no surface, and multiple surfaces.
- Duplicate point IDs, nonfinite coordinates, unresolved face references, repeated vertices, and degenerate triangles.
- Surface name, point count, face count, EPSG, unit, direction, and vertical-datum mismatches.

## Test strategy

Implementation is test-first. Each rule begins with a failing focused test or fixture, followed by the smallest validator behavior that makes it pass.

Tests call the library directly and separately exercise CLI rendering and exit codes. They reopen committed ZIPs from disk; unpacked fixture directories alone are not sufficient. Assertions target status and stable issue codes instead of brittle full prose, while focused renderer tests cover the human-readable warning and contract-valid disclaimer.

The ordinary test suite requires only .NET 8 and remains independent of ArcGIS Pro, Civil 3D, and Autodesk SDKs. A successful suite proves contract conformance, parsing safety, and report behavior. It does not close the later live Civil 3D import gate.

## Repository and Git sequencing

The configured `origin` is `https://github.com/0bnoxide/AutoGIS-Civil3D.git`. At design approval, local `main` has no commits and the diagnostic artifacts are untracked.

The sequence is:

1. Commit this approved design specification as the repository's first governance record.
2. Produce and approve a detailed implementation plan.
3. Add baseline documentation, diagnostic organization, and recorded hashes in a focused commit.
4. Add the schema, .NET solution, validator, CLI, fixtures, and tests through small test-first commits.
5. Run the repaired diagnostic and live Civil 3D import later when the required Autodesk environment is available; retain their evidence separately from contract-test results.

No push, producer integration, Autodesk adapter, or live-import claim is implied by the design-specification commit.

## Acceptance criteria for steps 1-4

Steps 1-4 are complete when:

- Repository baseline documentation and ADR-0001 explain ownership and dependency direction.
- Both diagnostic ZIP versions and the repaired source are preserved in their documented locations with verified hashes.
- .NET 8 SDK availability is recorded and the solution restores, builds, and tests successfully.
- The version-1 JSON Schema and semantic rules agree with this document.
- The library and CLI validate without Autodesk or ArcGIS dependencies.
- Every required valid and invalid golden ZIP produces its documented status and stable issue code.
- Malformed packages are handled as reports without extraction or uncontrolled parser behavior.
- Documentation clearly distinguishes contract validation from a real Civil 3D import test.

There are no unresolved design questions in this slice.
