using AutoGIS.Civil3D.Handoff.Validation;

namespace AutoGIS.Civil3D.Handoff.Cli;

internal static class CliApplication
{
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 1)
        {
            error.WriteLine("Usage: autogis-civil3d-handoff <bundle.zip>");
            return 3;
        }

        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(args[0]);
            TextReportRenderer.Write(report, output);
            return report.Status switch
            {
                ValidationStatus.Valid => 0,
                ValidationStatus.Invalid => 1,
                ValidationStatus.ValidWithWarnings => 2,
                _ => 3
            };
        }
        catch (Exception exception)
        {
            error.WriteLine($"Operational failure: {exception.Message}");
            return 3;
        }
    }
}
