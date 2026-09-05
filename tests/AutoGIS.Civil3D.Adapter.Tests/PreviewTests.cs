using AutoGIS.Civil3D.Proposal;
using Xunit;

namespace AutoGIS.Civil3D.Adapter.Tests;

public sealed class PreviewTests
{
    [Fact]
    public void ShowsFinalTargetInputsAndEveryActionOnceInPlanOrderWithManualLimits()
    {
        var manifest = StandardsManifest.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-standards.json"))).Manifest!;
        var inputs = new ProposalInputs("Synthetic Client", "Synthetic Site", 2026, "Landscape", "TEST-A1",
            "CLIENT-42", "PROJECT-43", "PROPOSAL-44", "Synthetic Address", "Synthetic Manager");
        var plan = ProposalPlanner.Build(inputs, manifest).Plan!;
        using var output = new StringWriter();
        ProposalPreview.Write(plan, output);
        string text = output.ToString();
        Assert.Equal(1, text.Split(plan.FinalRoot, StringSplitOptions.None).Length - 1);
        int previous = -1;
        foreach (var action in plan.Actions)
        {
            string heading = $"[{action.Id}] {action.Operation}: {action.RelativePath}";
            Assert.Equal(1, text.Split(heading, StringSplitOptions.None).Length - 1);
            int index = text.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(index > previous);
            previous = index;
        }
        string inputSection = text[..text.IndexOf("Actions:", StringComparison.Ordinal)];
        foreach (string value in new[] { "Synthetic Client", "Synthetic Site", "2026", "Landscape", "TEST-A1", "CLIENT-42", "PROJECT-43", "PROPOSAL-44", "Synthetic Address", "Synthetic Manager" })
            Assert.Contains(value, inputSection, StringComparison.Ordinal);
        foreach (string detail in new[] { "Model.dwt", "Sheet.dwt", "TEST-LAYOUT", "TEST-PAGE", "TEST-DEVICE", "TEST-MEDIA", "TEST.ctb", "SITE LOCATION", "SITE OVERVIEW", "Overlay", "TEST_CLIENTNAME" })
            Assert.Contains(detail, text, StringComparison.Ordinal);
        foreach (string limitation in new[] {
            "Execution unavailable in preview build",
            "Existing ground: pending; no surface is created.",
            "Data shortcuts: folder only; associate the project and publish a real surface manually.",
            "Viewports: placeholders only; framing and scale selection remain manual.",
            "Native template contents and Sheet Set operations still require Civil 3D qualification." })
            Assert.Equal(1, text.Split(limitation, StringSplitOptions.None).Length - 1);
    }
}
