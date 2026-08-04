namespace AutoGIS.Civil3D.Handoff.Tests;

internal static class TestRepository
{
    internal static string Root
    {
        get
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                bool hasSolution = File.Exists(Path.Combine(current.FullName, "AutoGIS.Civil3D.sln"));
                bool hasSpec = File.Exists(Path.Combine(
                    current.FullName,
                    "docs",
                    "superpowers",
                    "specs",
                    "2026-08-02-landxml-handoff-contract-design.md"));
                if (hasSolution && hasSpec) return current.FullName;
                current = current.Parent;
            }

            throw new InvalidOperationException("Could not locate the AutoGIS-Civil3D repository root.");
        }
    }
}
