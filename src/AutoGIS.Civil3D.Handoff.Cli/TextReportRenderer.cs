using AutoGIS.Civil3D.Handoff.Validation;

namespace AutoGIS.Civil3D.Handoff.Cli;

internal static class TextReportRenderer
{
    internal static void Write(ValidationReport report, TextWriter output)
    {
        output.WriteLine($"Status: {StatusText(report.Status)}");
        output.WriteLine("Issues:");
        if (report.Issues.Count == 0)
        {
            output.WriteLine("- None");
        }
        else
        {
            foreach (ValidationIssue issue in report.Issues)
            {
                output.WriteLine($"- [{issue.Code}] {issue.Severity}: {issue.Message}");
            }
        }

        output.WriteLine("Verified metadata:");
        if (report.Metadata is null)
        {
            output.WriteLine("- None");
        }
        else
        {
            output.WriteLine($"- Package ID: {report.Metadata.PackageId}");
            output.WriteLine($"- Surface name: {report.Metadata.SurfaceName}");
            output.WriteLine($"- Point count: {report.Metadata.PointCount}");
            output.WriteLine($"- Face count: {report.Metadata.FaceCount}");
            output.WriteLine($"- EPSG code: {report.Metadata.EpsgCode}");
        }

        output.WriteLine("Contract-valid is not equivalent to Civil 3D import-tested.");
    }

    private static string StatusText(ValidationStatus status) => status switch
    {
        ValidationStatus.Valid => "VALID",
        ValidationStatus.ValidWithWarnings => "VALID WITH WARNINGS",
        ValidationStatus.Invalid => "INVALID",
        _ => "UNKNOWN"
    };
}
