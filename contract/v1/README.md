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

## Semantic rules

The validator requires matching manifest and LandXML surface name, point count,
face count, horizontal EPSG code, and units. Horizontal and vertical manifest
units are exactly `metre`, `international_foot`, or `us_survey_foot`.
LandXML horizontal units map `meter` to `metre`, `foot` to
`international_foot`, and `USSurveyFoot` to `us_survey_foot`; elevation `feet`
accepts either foot definition, with the manifest as the authoritative choice.
Vertical direction is `positive_up`.

The raw `surface.landxml` SHA-256 must match the manifest. A known vertical
datum supplies authority, positive code, and name. An unknown datum is valid
only when declared as unknown; it produces a review warning and must be resolved
before import.

## Versioning

This directory and schema define v1 only. Any incompatible package shape,
meaning, or policy change requires a new explicitly versioned contract; v1
validators and fixtures retain v1 behavior. See the [fixture matrix](../../fixtures/v1/README.md) for executable examples and the [architecture](../../docs/architecture-handoff.md) for the adapter boundary.
