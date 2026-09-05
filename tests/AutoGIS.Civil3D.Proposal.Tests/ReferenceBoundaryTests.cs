using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace AutoGIS.Civil3D.Proposal.Tests;

public sealed class ReferenceBoundaryTests
{
    private static bool IsBanned(string name) => new[]
    {
        "Autodesk", "Aec", "acdbmgd", "acmgd", "AcCoreMgd", "ArcGIS", "ESRI",
        "AutoGIS.Civil3D.Adapter", "AutoGIS.Civil3D.Handoff"
    }.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData("Autodesk.AutoCAD.Runtime", true)]
    [InlineData("Autodesk.Civil.DatabaseServices", true)]
    [InlineData("AeccDbMgd", true)]
    [InlineData("AecBaseMgd", true)]
    [InlineData("acdbmgd", true)]
    [InlineData("acmgd", true)]
    [InlineData("AcCoreMgd", true)]
    [InlineData("ArcGIS.Core", true)]
    [InlineData("ESRI.ArcGIS.Geodatabase", true)]
    [InlineData("AutoGIS.Civil3D.Adapter", true)]
    [InlineData("AutoGIS.Civil3D.Handoff", true)]
    [InlineData("AutoGIS.Civil3D.Proposal", false)]
    [InlineData("System.Text.Json", false)]
    [InlineData("System.Collections.Immutable", false)]
    public void Detector_rejects_desktop_gis_adapter_and_handoff_names(string name, bool expected) =>
        Assert.Equal(expected, IsBanned(name));

    [Fact]
    public void Proposal_reference_closure_is_independent_of_desktop_and_handoff()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontier = new Queue<AssemblyName>(typeof(ProposalPlanner).Assembly.GetReferencedAssemblies());
        while (frontier.TryDequeue(out var current))
        {
            if (current.Name is not { } name || !seen.Add(name)) continue;
            Assembly loaded;
            try { loaded = Assembly.Load(current); }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException) { continue; }
            foreach (var dependency in loaded.GetReferencedAssemblies()) frontier.Enqueue(dependency);
        }
        Assert.Contains("System.Text.Json", seen);
        Assert.Contains("System.Collections.Immutable", seen);
        Assert.DoesNotContain(seen, IsBanned);
    }

    [Fact]
    public void Proposal_project_declares_no_project_package_or_desktop_assembly_dependency()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AutoGIS.Civil3D.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var project = XDocument.Load(Path.Combine(directory.FullName, "src", "AutoGIS.Civil3D.Proposal", "AutoGIS.Civil3D.Proposal.csproj"));
        Assert.DoesNotContain(project.Descendants(), e => e.Name.LocalName is "ProjectReference" or "PackageReference" or "Reference");
    }
}
