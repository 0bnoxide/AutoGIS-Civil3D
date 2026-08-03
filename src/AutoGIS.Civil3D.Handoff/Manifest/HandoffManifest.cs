using AutoGIS.Civil3D.Handoff.Validation;

namespace AutoGIS.Civil3D.Handoff.Manifest;

internal enum LinearUnit
{
    Metre,
    InternationalFoot,
    UsSurveyFoot
}

internal enum VerticalDatumStatus
{
    Known,
    Unknown
}

internal sealed record HandoffManifest(
    string ContractVersion,
    Guid PackageId,
    DateTimeOffset CreatedUtc,
    ProducerManifest Producer,
    SurfaceManifest Surface,
    CoordinateReferenceManifest CoordinateReference);

internal sealed record ProducerManifest(string Name, string Version, string? SourceCommit);

internal sealed record SurfaceManifest(
    string Filename,
    string Sha256,
    string LandxmlVersion,
    string Name,
    long PointCount,
    long FaceCount);

internal sealed record CoordinateReferenceManifest(
    HorizontalReferenceManifest Horizontal,
    VerticalReferenceManifest Vertical);

internal sealed record HorizontalReferenceManifest(int Code, LinearUnit Unit);

internal sealed record VerticalReferenceManifest(
    LinearUnit Unit,
    VerticalDatumManifest Datum);

internal sealed record VerticalDatumManifest(
    VerticalDatumStatus Status,
    string? Authority,
    int? Code,
    string? Name,
    string? Note);

internal sealed record ManifestParseResult(
    HandoffManifest? Manifest,
    IReadOnlyList<ValidationIssue> Issues);
