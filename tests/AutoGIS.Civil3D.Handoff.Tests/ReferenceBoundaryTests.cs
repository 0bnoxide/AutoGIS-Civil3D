using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AutoGIS.Civil3D.Handoff.Manifest;
using Xunit;

namespace AutoGIS.Civil3D.Handoff.Tests;

// Guards the ADR-0001 seam: "ArcGIS and Autodesk references cannot enter the
// core validation library." AutoGIS.Civil3D.Handoff must stay buildable and
// testable without either desktop application, so no Autodesk (AutoCAD /
// Civil 3D) or Esri assembly may appear anywhere in its reference closure.
// The boundary is clean today; this test fails the build if that ever changes.
public sealed class ReferenceBoundaryTests
{
    // Assembly-name prefixes that would dissolve the seam. Autodesk desktop
    // APIs ship as Autodesk.* / Aecc* and the AutoCAD managed assemblies
    // acdbmgd / acmgd / AcCoreMgd; Esri as ArcGIS* / ESRI*. Matched
    // case-insensitively (the AutoCAD names vary in casing).
    private static readonly string[] BannedPrefixes =
    {
        "Autodesk", "Aecc", "acdbmgd", "acmgd", "AcCoreMgd", "ArcGIS", "ESRI",
    };

    private static bool IsBanned(string assemblyName) =>
        BannedPrefixes.Any(prefix =>
            assemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    // The detector has teeth in isolation: it flags the desktop/GIS assembly
    // names and clears the assemblies the library legitimately depends on.
    [Theory]
    [InlineData("Autodesk.AutoCAD.Runtime", true)]
    [InlineData("Autodesk.Civil.DatabaseServices", true)]
    [InlineData("AeccDbMgd", true)]
    [InlineData("acdbmgd", true)]
    [InlineData("acmgd", true)]
    [InlineData("AcCoreMgd", true)]
    [InlineData("ArcGIS.Core", true)]
    [InlineData("ESRI.ArcGIS.Geodatabase", true)]
    [InlineData("JsonSchema.Net", false)]
    [InlineData("ICSharpCode.SharpZipLib", false)]
    [InlineData("System.Text.Json", false)]
    [InlineData("AutoGIS.Civil3D.Handoff", false)]
    public void IsBanned_flags_only_desktop_and_gis_assemblies(string name, bool expected) =>
        Assert.Equal(expected, IsBanned(name));

    [Fact]
    public void Handoff_reference_closure_admits_no_desktop_or_gis_assembly()
    {
        ISet<string> closure = ReferenceClosureOf(typeof(ManifestParser).Assembly);

        // Teeth: prove the walk actually traversed Handoff's own reference graph
        // rather than passing vacuously. Its two declared dependencies must
        // appear (SharpZipLib ships as the ICSharpCode.SharpZipLib assembly).
        Assert.Contains("JsonSchema.Net", closure, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ICSharpCode.SharpZipLib", closure, StringComparer.OrdinalIgnoreCase);

        List<string> offenders = closure.Where(IsBanned)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.True(
            offenders.Count == 0,
            "AutoGIS.Civil3D.Handoff must not reference Autodesk or Esri assemblies " +
            "(ADR-0001). Found: " + string.Join(", ", offenders));
    }

    // Breadth-first walk of the referenced-assembly graph, keyed by simple
    // name. GetReferencedAssemblies() reports a referrer's dependencies by name
    // without loading them, so a banned assembly is caught by name even when it
    // is absent from the test host; Load only expands the frontier, and a node
    // that cannot be loaded still contributes its own name.
    //
    // ponytail: reflection closure only — the C# compiler drops a
    // declared-but-unused PackageReference from GetReferencedAssemblies(), so a
    // banned package referenced without using a type would slip past. Add a
    // project.assets.json (or `dotnet list package`) scan if guarding the
    // declaration, not just the loaded closure, becomes a real concern.
    private static ISet<string> ReferenceClosureOf(Assembly root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontier = new Queue<AssemblyName>(root.GetReferencedAssemblies());

        while (frontier.Count > 0)
        {
            AssemblyName current = frontier.Dequeue();
            string? name = current.Name;
            if (name is null || !seen.Add(name))
            {
                continue;
            }

            Assembly loaded;
            try
            {
                loaded = Assembly.Load(current);
            }
            catch
            {
                // Unresolvable (e.g. a platform assembly absent from this host):
                // the name is already recorded; we just cannot expand it.
                continue;
            }

            foreach (AssemblyName next in loaded.GetReferencedAssemblies())
            {
                frontier.Enqueue(next);
            }
        }

        return seen;
    }
}
