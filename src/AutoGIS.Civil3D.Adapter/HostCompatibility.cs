namespace AutoGIS.Civil3D.Adapter;

public static class HostCompatibility
{
    public static bool IsSupported(Version? autocad, Version? civil, bool is64Bit) =>
        is64Bit && autocad is { Major: 25, Minor: 1 } && civil is { Major: 13, Minor: 8 };
}
