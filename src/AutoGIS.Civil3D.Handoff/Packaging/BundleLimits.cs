namespace AutoGIS.Civil3D.Handoff.Packaging;

internal static class BundleLimits
{
    internal const long ManifestBytes = 1L * 1024 * 1024;
    internal const long SurfaceBytes = 2L * 1024 * 1024 * 1024;
    internal const double MaximumCompressionRatio = 100d;
    internal const int EntryCount = 2;
}
