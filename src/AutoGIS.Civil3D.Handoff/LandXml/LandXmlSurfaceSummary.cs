using AutoGIS.Civil3D.Handoff.Manifest;
using AutoGIS.Civil3D.Handoff.Validation;

namespace AutoGIS.Civil3D.Handoff.LandXml;

internal enum VerticalUnitFamily
{
    Metre,
    Foot
}

internal readonly record struct Point3(double Northing, double Easting, double Elevation);

internal sealed record LandXmlSurfaceSummary(
    string LandxmlVersion,
    string SurfaceName,
    long PointCount,
    long FaceCount,
    int EpsgCode,
    LinearUnit HorizontalUnit,
    VerticalUnitFamily VerticalUnitFamily);

internal sealed record LandXmlParseResult(
    LandXmlSurfaceSummary? Summary,
    IReadOnlyList<ValidationIssue> Issues);
