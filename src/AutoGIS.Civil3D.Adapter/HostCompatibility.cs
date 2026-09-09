namespace AutoGIS.Civil3D.Adapter;

public static class HostCompatibility
{
    public static bool IsSupported(Version? autocad, Version? civil, bool is64Bit) =>
        is64Bit && autocad is { Major: 25, Minor: 1 } && civil is { Major: 13, Minor: 8 };

    internal static string BindingReport(Version? autocad, Version? civil, bool is64Bit) =>
        $"AUTOGISPROPOSALSMOKE {(IsSupported(autocad, civil, is64Bit) ? "PASS" : "FAIL")}: " +
        "target Civil 3D 2026 (AutoCAD 25.1 / Civil 13.8, x64); " +
        $"observed AutoCAD {autocad?.ToString() ?? "unavailable"}, Civil {civil?.ToString() ?? "unavailable"}, " +
        $"{(is64Bit ? "x64" : "not x64")}. Host binding only; proposal creation was not tested.";
}
