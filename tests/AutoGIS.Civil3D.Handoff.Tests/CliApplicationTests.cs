using AutoGIS.Civil3D.Handoff.Cli;
using AutoGIS.Civil3D.Handoff.Manifest;
using Xunit;

namespace AutoGIS.Civil3D.Handoff.Tests;

public sealed class CliApplicationTests
{
    [Fact]
    public void No_arguments_exits_three_and_writes_usage_to_standard_error()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exitCode = CliApplication.Run([], stdout, stderr);

        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal("Usage: autogis-civil3d-handoff <bundle.zip>" + Environment.NewLine, stderr.ToString());
    }

    [Fact]
    public void Too_many_arguments_exits_three_and_writes_usage_to_standard_error()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exitCode = CliApplication.Run(["first.zip", "second.zip"], stdout, stderr);

        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Equal("Usage: autogis-civil3d-handoff <bundle.zip>" + Environment.NewLine, stderr.ToString());
    }

    [Fact]
    public void Missing_file_exits_three_and_writes_operational_failure_to_standard_error()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.zip");
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exitCode = CliApplication.Run([path], stdout, stderr);

        Assert.Equal(3, exitCode);
        Assert.Equal(string.Empty, stdout.ToString());
        Assert.StartsWith("Operational failure: ", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_package_exits_zero_and_renders_verified_metadata()
    {
        string path = TestPackageBuilder.CreateValid(VerticalDatumStatus.Known);
        StringWriter stdout = new();
        StringWriter stderr = new();

        try
        {
            int exitCode = CliApplication.Run([path], stdout, stderr);

            Assert.Equal(0, exitCode);
            string report = stdout.ToString();
            Assert.StartsWith("Status: VALID" + Environment.NewLine, report, StringComparison.Ordinal);
            Assert.Contains("- Package ID: 9a8ff271-b0d8-46db-809d-a6f72954af20", report);
            Assert.Contains("- Surface name: Existing Ground", report);
            Assert.Contains("- Point count: 3", report);
            Assert.Contains("- Face count: 1", report);
            Assert.Contains("- EPSG code: 26913", report);
            Assert.EndsWith(
                "Contract-valid is not equivalent to Civil 3D import-tested." + Environment.NewLine,
                report,
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Warning_package_exits_two_and_requires_human_review()
    {
        string path = TestPackageBuilder.CreateValid(VerticalDatumStatus.Unknown);
        StringWriter stdout = new();
        StringWriter stderr = new();

        try
        {
            int exitCode = CliApplication.Run([path], stdout, stderr);

            Assert.Equal(2, exitCode);
            string report = stdout.ToString();
            Assert.StartsWith(
                "Status: VALID WITH WARNINGS" + Environment.NewLine,
                report,
                StringComparison.Ordinal);
            Assert.Contains("[WRN001] Warning:", report);
            Assert.EndsWith(
                "Contract-valid is not equivalent to Civil 3D import-tested." + Environment.NewLine,
                report,
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Invalid_package_exits_one_and_renders_validation_issue()
    {
        string path = TestPackageBuilder.Create(PackageFault.Malformed);
        StringWriter stdout = new();
        StringWriter stderr = new();

        try
        {
            int exitCode = CliApplication.Run([path], stdout, stderr);

            Assert.Equal(1, exitCode);
            string report = stdout.ToString();
            Assert.StartsWith("Status: INVALID" + Environment.NewLine, report, StringComparison.Ordinal);
            Assert.Contains("[ZIP001] Error:", report);
            Assert.EndsWith(
                "Contract-valid is not equivalent to Civil 3D import-tested." + Environment.NewLine,
                report,
                StringComparison.Ordinal);
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_deflated_entry_exits_one_as_invalid_package()
    {
        string path = TestPackageBuilder.Create(PackageFault.CorruptDeflatedSurface);
        StringWriter stdout = new();
        StringWriter stderr = new();

        try
        {
            int exitCode = CliApplication.Run([path], stdout, stderr);

            Assert.Equal(1, exitCode);
            Assert.Contains("[ZIP001] Error:", stdout.ToString());
            Assert.Equal(string.Empty, stderr.ToString());
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }
}
