# Version 1 LandXML handoff contract

`contract/v1/handoff-manifest.schema.json` is the normative structural schema for
contract version `1.0`. The package is one ZIP with exactly two regular files at
its root, with exact case-sensitive names:

```text
handoff.json
surface.landxml
```

Version 1 carries one LandXML 1.2 TIN surface only. It does not support
directories, attachments, multiple surfaces, DWG/DXF, signing, encryption, or
coordinate/elevation/datum conversion.

## Structural rules

`handoff.json` is UTF-8 JSON that conforms to the schema. The schema fixes the
contract version, manifest property names, required metadata, one surface file,
SHA-256 digest shape, and coordinate-reference shape. Objects reject additional
properties. Package names, manifest field names, and allowed values are exact;
consumers must not normalize or infer alternatives.

ZIP entries must be regular root files, stored or deflated, and have no unsafe,
duplicate, or case-colliding paths. The v1 limits are exactly two entries, a
1 MiB uncompressed `handoff.json`, a 2 GiB uncompressed `surface.landxml`, and
a maximum 100:1 per-entry compression ratio.

Central-directory metadata is not trusted by itself. Each entry's local header
must agree with the central record on flags, method, and name. Ordinary entries
also match CRC and sizes there; descriptor entries match them in a valid
ZIP32/ZIP64 data descriptor whose compressed size equals the physical data span.
Local extra fields are bounded, ZIP64 records are reconciled, and streaming
verifies actual size and incremental CRC-32. These checks reject ZIP parser
differentials and forged-size ratio bypasses without extracting the archive.

## Semantic rules

`package_id` uses the canonical hyphenated UUID form. `created_utc` is a valid
date-time normalized to UTC with a trailing `Z`. Producer name and version are
nonblank, trimmed, control-character-free values and must not contain rooted or
relative filesystem paths, path separators, traversal segments, or drive-relative
syntax. When present, `producer.source_commit` is 7-64 lowercase hexadecimal
characters.

The validator requires matching manifest and LandXML surface name, point count,
face count, horizontal EPSG code, and units. Horizontal and vertical manifest
units are exactly `metre`, `international_foot`, or `us_survey_foot`.
LandXML horizontal units map `meter` to `metre`, `foot` to
`international_foot`, and `USSurveyFoot` to `us_survey_foot`; elevation `feet`
accepts either foot definition, with the manifest as the authoritative choice.
Vertical direction is `positive_up`.

LandXML parsing prohibits document type declarations and external resource
resolution. Every TIN point has one unique positive integer identifier and exactly
three finite coordinates in northing, easting, elevation order. Every face resolves
to three existing, distinct point identifiers. A face is invalid when its projected
horizontal vertices coincide or its absolute 2D cross product is at most `1e-12`
times its largest squared edge length.

Validation is fail-fast by layer. Within LandXML, envelope, surface, and
TIN-definition checks have precedence; otherwise the first point or face error
encountered in document order is the single primary issue. Co-occurring errors
are not sorted by issue code.

The raw `surface.landxml` SHA-256 must match the manifest. A known vertical
datum supplies authority, positive code, and name. An unknown datum is valid
only when declared as unknown; it produces a review warning, and elevation
alignment must be confirmed before use.

## Versioning

This directory and schema define v1 only. Any incompatible package shape,
meaning, or policy change requires a new explicitly versioned contract; v1
validators and fixtures retain v1 behavior. See the [fixture matrix](../../fixtures/v1/README.md) for executable examples and the [architecture](../../docs/architecture-handoff.md) for the adapter boundary.
