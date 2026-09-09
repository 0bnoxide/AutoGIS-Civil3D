using Xunit;

namespace AutoGIS.Civil3D.Adapter.Tests;

public sealed class HostCompatibilityTests
{
    [Theory]
    [InlineData("25.1.0.0", "13.8.0.1516", true, true)]
    [InlineData("25.1.99.0", "13.8.99.0", true, true)]
    [InlineData("25.0.0.0", "13.8.0.0", true, false)]
    [InlineData("25.1.0.0", "13.7.0.0", true, false)]
    [InlineData("24.3.0.0", "13.6.0.0", true, false)]
    [InlineData("25.1.0.0", "13.8.0.0", false, false)]
    [InlineData(null, "13.8.0.0", true, false)]
    [InlineData("25.1.0.0", null, true, false)]
    public void AllowsOnly2026SeriesIn64BitHost(string? autocad, string? civil, bool is64Bit, bool expected)
    {
        Assert.Equal(expected, HostCompatibility.IsSupported(
            autocad is null ? null : Version.Parse(autocad),
            civil is null ? null : Version.Parse(civil), is64Bit));
    }

    [Theory]
    [InlineData("25.1.0.0", "13.8.0.1516", true, "PASS", "observed AutoCAD 25.1.0.0, Civil 13.8.0.1516, x64")]
    [InlineData("25.0.0.0", null, true, "FAIL", "observed AutoCAD 25.0.0.0, Civil unavailable, x64")]
    public void BindingReportNamesResultTargetAndObservedHost(
        string autocad, string? civil, bool is64Bit, string result, string observation)
    {
        string report = HostCompatibility.BindingReport(
            Version.Parse(autocad), civil is null ? null : Version.Parse(civil), is64Bit);

        Assert.Contains($"AUTOGISPROPOSALSMOKE {result}", report, StringComparison.Ordinal);
        Assert.Contains("target Civil 3D 2026 (AutoCAD 25.1 / Civil 13.8, x64)", report, StringComparison.Ordinal);
        Assert.Contains(observation, report, StringComparison.Ordinal);
        Assert.Contains("Host binding only; proposal creation was not tested.", report, StringComparison.Ordinal);
    }
}
