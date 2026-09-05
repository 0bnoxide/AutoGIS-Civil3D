using AutoGIS.Civil3D.Proposal;
using Xunit;

namespace AutoGIS.Civil3D.Adapter.Tests;

public sealed class PreviewTests(Xunit.Abstractions.ITestOutputHelper testOutput)
{
    [Fact]
    public void ShowsFinalTargetInputsAndEveryActionOnceInPlanOrderWithManualLimits()
    {
        var manifest = StandardsManifest.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-standards.json"))).Manifest!;
        var inputs = new ProposalInputs("Synthetic Client", "Synthetic Site", 2026, "Landscape", "TEST-A1",
            "CLIENT-42", "PROJECT-43", "PROPOSAL-44", "Synthetic Address", "Synthetic Manager");
        var plan = ProposalPlanner.Build(inputs, manifest).Plan!;
        string canonicalPlan = plan.ToJson();
        using var output = new StringWriter();
        ProposalPreview.Write(plan, output);
        string text = output.ToString();
        testOutput.WriteLine($"Rendered {plan.Actions.Length} actions in {text.Split(Environment.NewLine).Length} lines.");
        Assert.Equal(1, text.Split(plan.FinalRoot, StringSplitOptions.None).Length - 1);
        int previous = -1;
        foreach (var action in plan.Actions)
        {
            string heading = $"[{action.Id}] ";
            Assert.Equal(1, text.Split(heading, StringSplitOptions.None).Length - 1);
            int index = text.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(index > previous);
            Assert.Contains(action.RelativePath, text[index..].Split(Environment.NewLine)[0], StringComparison.Ordinal);
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
        {
            Assert.Equal(1, text.Split(limitation, StringSplitOptions.None).Length - 1);
            Assert.True(text.IndexOf(limitation, StringComparison.Ordinal) < text.IndexOf("Actions:", StringComparison.Ordinal));
        }
        foreach (string description in new[] {
            "Create folder: Model", "Create model drawing: Model/Base.dwg",
            "Create sheet drawing: Sheets/TEST-01.dwg", "Sheet TEST-01: Site Overview / Site Location",
            "Create sheet set: Proposal.dst", "Set sheet set properties: Proposal.dst",
            "Register sheet TEST-01", "Bind title block TEST-TITLE", "Add Overlay Xref",
            "Write project configuration", "Create empty source register", "Create empty assumptions log",
            "Create empty decision log", "Write creation plan", "Write creation receipt after verification",
            "Client name → TEST-ClientName: Synthetic Client", "TEST-ClientName → TEST_CLIENTNAME",
            "Page setup: TEST-PAGE", "Plot: TEST-DEVICE; media TEST-MEDIA; style TEST.ctb",
            "units Millimeters; scale 1; rotation 0", "Printable area: x 0, y 0, width 800, height 550",
            "SITE LOCATION (Boundary): x 10, y 10, width 180, height 240",
            "Reference: ../Model/Base.dwg", "Insertion: (0, 0, 0); scale 1; rotation 0" })
            Assert.Contains(description, text, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateModelDrawing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        Assert.DoesNotContain("}", text, StringComparison.Ordinal);
        Assert.Equal(1, text.Split("SITE LOCATION (Boundary)", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, text.Split("SITE OVERVIEW (Boundary)", StringSplitOptions.None).Length - 1);
        Assert.True(text.Split(Environment.NewLine).Length < 200, "The starter plan should remain a compact review, not a DTO dump.");
        Assert.Equal(canonicalPlan, plan.ToJson());
    }

    [Fact]
    public void AbsentOptionalInputsAreReadableAndConfiguredTransformsAreRetained()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-standards.json")))!;
        json["xrefs"]![0]!["insertion"]!["x"] = 12.5;
        json["xrefs"]![0]!["scale"] = 0.75;
        json["xrefs"]![0]!["rotation"] = 0.25;
        var manifest = StandardsManifest.Parse(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())).Manifest!;
        var plan = ProposalPlanner.Build(new("Synthetic Client", "Synthetic Site", 2026, "Landscape", "TEST-A1"), manifest).Plan!;
        using var output = new StringWriter();
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            ProposalPreview.Write(plan, output);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
        string text = output.ToString();
        foreach (string label in new[] { "Client number", "Official project number", "Proposal number / identifier", "Site address", "Project manager" })
            Assert.Contains($"{label}: (not supplied)", text, StringComparison.Ordinal);
        Assert.Contains("Client number → TEST-ClientNumber: (not supplied)", text, StringComparison.Ordinal);
        Assert.Contains("Insertion: (12.5, 0, 0); scale 0.75; rotation 0.25", text, StringComparison.Ordinal);
        Assert.DoesNotContain("null", text, StringComparison.Ordinal);
    }
}
