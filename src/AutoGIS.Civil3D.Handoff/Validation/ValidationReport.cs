namespace AutoGIS.Civil3D.Handoff.Validation;

public sealed record VerifiedPackageMetadata(
    Guid PackageId,
    string SurfaceName,
    long PointCount,
    long FaceCount,
    int EpsgCode);

public sealed record ValidationReport(
    ValidationStatus Status,
    IReadOnlyList<ValidationIssue> Issues,
    VerifiedPackageMetadata? Metadata);
