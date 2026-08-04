using System.Security.Cryptography;
using AutoGIS.Civil3D.Handoff.LandXml;
using AutoGIS.Civil3D.Handoff.Manifest;
using AutoGIS.Civil3D.Handoff.Packaging;
using AutoGIS.Civil3D.Handoff.Validation;

namespace AutoGIS.Civil3D.Handoff;

public sealed class BundleValidator
{
    public ValidationReport ValidateBundle(string path)
    {
        BundleOpenResult openResult = BundleArchive.Open(path);
        if (openResult.Archive is null)
        {
            return BuildReport(openResult.Issues);
        }

        using BundleArchive archive = openResult.Archive;
        List<ValidationIssue> issues = [.. openResult.Issues];

        if (!TryRunStage(
                () => ManifestParser.Parse(archive.ReadManifestBytes()),
                issues,
                out ManifestParseResult manifestResult))
        {
            return BuildReport(issues);
        }

        issues.AddRange(manifestResult.Issues);
        if (HasErrors(issues))
        {
            return BuildReport(issues);
        }

        HandoffManifest manifest = manifestResult.Manifest!;
        if (!TryRunStage(
                () =>
                {
                    using Stream surfaceStream = archive.OpenSurfaceStream();
                    return Convert.ToHexString(SHA256.HashData(surfaceStream)).ToLowerInvariant();
                },
                issues,
                out string actualChecksum))
        {
            return BuildReport(issues);
        }

        if (!string.Equals(actualChecksum, manifest.Surface.Sha256, StringComparison.Ordinal))
        {
            issues.Add(
                Error(
                    IssueCodes.ChecksumMismatch,
                    "surface.landxml does not match the manifest SHA-256 digest.",
                    "surface.sha256"));
            return BuildReport(issues);
        }

        if (!TryRunStage(
                () =>
                {
                    using Stream surfaceStream = archive.OpenSurfaceStream();
                    return LandXmlSurfaceParser.Parse(surfaceStream);
                },
                issues,
                out LandXmlParseResult landXmlResult))
        {
            return BuildReport(issues);
        }

        issues.AddRange(landXmlResult.Issues);
        if (HasErrors(issues))
        {
            return BuildReport(issues);
        }

        LandXmlSurfaceSummary summary = landXmlResult.Summary!;
        AddCrossCheckIssues(manifest, summary, issues);
        if (HasErrors(issues))
        {
            return BuildReport(issues);
        }

        VerifiedPackageMetadata metadata = new(
            manifest.PackageId,
            summary.SurfaceName,
            summary.PointCount,
            summary.FaceCount,
            summary.EpsgCode);
        return BuildReport(issues, metadata);
    }

    private static void AddCrossCheckIssues(
        HandoffManifest manifest,
        LandXmlSurfaceSummary summary,
        ICollection<ValidationIssue> issues)
    {
        if (!string.Equals(manifest.Surface.Name, summary.SurfaceName, StringComparison.Ordinal))
        {
            issues.Add(
                Error(
                    IssueCodes.SurfaceNameMismatch,
                    "The manifest surface name does not match LandXML.",
                    "surface.name"));
        }

        if (manifest.Surface.PointCount != summary.PointCount)
        {
            issues.Add(
                Error(
                    IssueCodes.PointCountMismatch,
                    "The manifest point count does not match LandXML.",
                    "surface.point_count"));
        }

        if (manifest.Surface.FaceCount != summary.FaceCount)
        {
            issues.Add(
                Error(
                    IssueCodes.FaceCountMismatch,
                    "The manifest face count does not match LandXML.",
                    "surface.face_count"));
        }

        if (manifest.CoordinateReference.Horizontal.Code != summary.EpsgCode)
        {
            issues.Add(
                Error(
                    IssueCodes.EpsgMismatch,
                    "The manifest EPSG code does not match LandXML.",
                    "coordinate_reference.horizontal.code"));
        }

        if (manifest.CoordinateReference.Horizontal.Unit != summary.HorizontalUnit)
        {
            issues.Add(
                Error(
                    IssueCodes.HorizontalUnitMismatch,
                    "The manifest horizontal unit does not match LandXML.",
                    "coordinate_reference.horizontal.unit"));
        }

        if (!VerticalUnitsMatch(
                manifest.CoordinateReference.Vertical.Unit,
                summary.VerticalUnitFamily))
        {
            issues.Add(
                Error(
                    IssueCodes.VerticalUnitFamilyMismatch,
                    "The manifest vertical unit family does not match LandXML.",
                    "coordinate_reference.vertical.unit"));
        }
    }

    private static bool VerticalUnitsMatch(
        LinearUnit manifestUnit,
        VerticalUnitFamily landXmlFamily) =>
        landXmlFamily switch
        {
            VerticalUnitFamily.Metre => manifestUnit == LinearUnit.Metre,
            VerticalUnitFamily.Foot => manifestUnit is LinearUnit.InternationalFoot or LinearUnit.UsSurveyFoot,
            _ => false
        };

    private static bool TryRunStage<T>(
        Func<T> stage,
        List<ValidationIssue> issues,
        out T result)
    {
        try
        {
            result = stage();
            return true;
        }
        catch (BundleLimitExceededException)
        {
            issues.Add(StreamLimitIssue());
        }
        catch (BundleEntryDataException exception)
        {
            issues.Add(EntryDataIssue(exception));
        }

        result = default!;
        return false;
    }

    private static ValidationIssue StreamLimitIssue() =>
        Error(
            IssueCodes.StreamLimitExceeded,
            "A ZIP entry exceeded its runtime streaming limit.");

    private static ValidationIssue EntryDataIssue(BundleEntryDataException exception) =>
        Error(exception.Code, exception.Message);

    private static ValidationIssue Error(string code, string message, string? location = null) =>
        new(code, IssueSeverity.Error, message, location);

    private static bool HasErrors(IEnumerable<ValidationIssue> issues) =>
        issues.Any(issue => issue.Severity == IssueSeverity.Error);

    private static ValidationReport BuildReport(
        IReadOnlyList<ValidationIssue> issues,
        VerifiedPackageMetadata? metadata = null)
    {
        ValidationStatus status = HasErrors(issues)
            ? ValidationStatus.Invalid
            : issues.Any(issue => issue.Severity == IssueSeverity.Warning)
                ? ValidationStatus.ValidWithWarnings
                : ValidationStatus.Valid;
        return new ValidationReport(status, issues, metadata);
    }
}
