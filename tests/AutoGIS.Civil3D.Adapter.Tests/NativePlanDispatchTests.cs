using AutoGIS.Civil3D.Proposal;
using Xunit;

namespace AutoGIS.Civil3D.Adapter.Tests;

public sealed class NativePlanDispatchTests
{
    [Fact]
    public void ModelActionsMatchTheInspectedPlanAndStayInsideStaging()
    {
        var plan = Plan();
        string stage = Path.Combine(Path.GetTempPath(), "AutoGIS-native-dispatch", Guid.NewGuid().ToString("N"));

        foreach (var action in plan.Actions.Where(a => a.Operation == ProposalOperation.CreateFolder ||
            a.Operation == ProposalOperation.CreateModelDrawing ||
            a.Operation == ProposalOperation.AddXref && a.Data is XrefData { Reference.HostRole: "ProposedDesignModel" }))
            NativeProposalHost.ValidateModelAction(plan, action, stage);

        var other = ProposalPlanner.Build(plan.Inputs with { SiteName = "Another Site" }, plan.Configuration.Standards).Plan!;
        Assert.Throws<InvalidDataException>(() => NativeProposalHost.ValidateModelAction(plan,
            other.Actions.Single(a => a.Id == "20:model/BaseModel"), stage));
        Assert.Throws<NotSupportedException>(() => NativeProposalHost.ValidateModelAction(plan,
            plan.Actions.First(a => a.Operation == ProposalOperation.CreateSheetDrawing), stage));
    }

    [Fact]
    public void FullPlanRefusesBeforeAnyNativeOrFilesystemMutation()
    {
        var plan = Plan();
        var host = new NativeProposalHost();
        string stage = Path.Combine(Path.GetTempPath(), "AutoGIS-native-dispatch", Guid.NewGuid().ToString("N"));

        Assert.NotEmpty(host.Inspect(plan).Issues);
        Assert.Throws<NotSupportedException>(() => host.Apply(
            plan.Actions.First(a => a.Operation == ProposalOperation.CreateSheetDrawing), stage));
        Assert.False(Directory.Exists(stage));
    }

    [Fact]
    public void OverlayTargetMustResolveInsideStaging()
    {
        string stage = Path.Combine(Path.GetTempPath(), "AutoGIS-native-dispatch", Guid.NewGuid().ToString("N"));
        string target = NativeProposalHost.ResolveOverlayTarget(stage, "Model/C-SP Linework.dwg", "Base.dwg");
        Assert.Equal(ProposalFiles.Child(stage, "Model/Base.dwg"), target);
        Assert.Throws<ProposalConditionException>(() => NativeProposalHost.ResolveOverlayTarget(
            stage, "Model/C-SP Linework.dwg", "../../foreign.dwg"));
    }

    [Fact]
    public void TiltedXrefNormalIsNotThePlannedWorldUpTransform()
    {
        Assert.True(NativeProposalVerifier.HasWorldNormal(0, 0, 1));
        Assert.True(NativeProposalVerifier.HasWorldNormal(1e-12, -1e-12, 1 - 1e-12));
        Assert.False(NativeProposalVerifier.HasWorldNormal(0, 1, 0));
        Assert.False(NativeProposalVerifier.HasWorldNormal(1e-6, 0, 1));
        Assert.False(NativeProposalVerifier.HasWorldNormal(double.NaN, 0, 1));
    }

    private static ProposalPlan Plan()
    {
        byte[] manifestBytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-standards.json"));
        var manifest = StandardsManifest.Parse(manifestBytes).Manifest!;
        return ProposalPlanner.Build(new("Synthetic Client", "Synthetic Site", 2026, "Landscape", "TEST-A1"), manifest).Plan!;
    }
}
