using Xunit;

namespace AutoGIS.Civil3D.Adapter.Tests;

public sealed class HostCompatibilityTests
{
    [Theory]
    [InlineData("25.0.0.0", "13.7.0.154", true, true)]
    [InlineData("25.0.99.0", "13.7.99.0", true, true)]
    [InlineData("25.1.0.0", "13.7.0.0", true, false)]
    [InlineData("25.0.0.0", "13.8.0.0", true, false)]
    [InlineData("24.3.0.0", "13.6.0.0", true, false)]
    [InlineData("25.0.0.0", "13.7.0.0", false, false)]
    [InlineData(null, "13.7.0.0", true, false)]
    [InlineData("25.0.0.0", null, true, false)]
    public void AllowsOnlyRetainedSeriesIn64BitHost(string? autocad, string? civil, bool is64Bit, bool expected)
    {
        Assert.Equal(expected, HostCompatibility.IsSupported(
            autocad is null ? null : Version.Parse(autocad),
            civil is null ? null : Version.Parse(civil), is64Bit));
    }
}
