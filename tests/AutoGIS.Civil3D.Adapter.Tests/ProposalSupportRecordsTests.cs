using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoGIS.Civil3D.Proposal;
using Xunit;

namespace AutoGIS.Civil3D.Adapter.Tests;

public sealed class ProposalSupportRecordsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "AutoGIS-support-tests", Guid.NewGuid().ToString("N"));
    private readonly StandardsManifest standards;
    private readonly ProposalPlan plan;
    private readonly string stage;

    public ProposalSupportRecordsTests()
    {
        Directory.CreateDirectory(root);
        var manifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-standards.json")))!;
        manifest["baseRoots"]![0]!["path"] = Path.Combine(root, "Proposals").Replace('\\', '/');
        manifest["modelTemplate"] = Path.Combine(root, "Model.dwt").Replace('\\', '/');
        manifest["profiles"]![0]!["template"] = Path.Combine(root, "Sheet.dwt").Replace('\\', '/');
        var parsed = StandardsManifest.Parse(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
        standards = Assert.IsType<StandardsManifest>(parsed.Manifest);
        plan = Assert.IsType<ProposalPlan>(ProposalPlanner.Build(
            new("Synthetic Client", "Synthetic Site", 2026, "Landscape", "TEST-A1"),
            standards).Plan);
        stage = Path.Combine(root, "stage");
        Directory.CreateDirectory(stage);
    }

    [Theory]
    [InlineData(SupportContent.ProjectConfiguration)]
    [InlineData(SupportContent.EmptySourceRegister)]
    [InlineData(SupportContent.EmptyAssumptionsLog)]
    [InlineData(SupportContent.EmptyDecisionLog)]
    [InlineData(SupportContent.CreationPlan)]
    public void WritesExpectedJsonAndVerifiesActualBytes(SupportContent content)
    {
        var action = Action(content);
        string path = PathFor(action);
        ProposalSupportRecords.Write(plan, action, stage);
        byte[] bytes = File.ReadAllBytes(path);
        using var document = JsonDocument.Parse(bytes);
        string expected = content switch
        {
            SupportContent.ProjectConfiguration => ConfigurationJson(),
            SupportContent.CreationPlan => plan.ToJson(),
            _ => "[]"
        };
        Assert.Equal(Encoding.UTF8.GetBytes(expected), bytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), ProposalSupportRecords.Readback(plan, action, stage));
    }

    [Fact]
    public void ExistingFileIsNeverOverwritten()
    {
        var action = Action(SupportContent.EmptySourceRegister);
        string path = PathFor(action);
        byte[] original = [1, 2, 3];
        File.WriteAllBytes(path, original);
        Assert.Throws<IOException>(() => ProposalSupportRecords.Write(plan, action, stage));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void ReadbackRejectsTampering()
    {
        var action = Action(SupportContent.CreationPlan);
        string path = PathFor(action);
        ProposalSupportRecords.Write(plan, action, stage);
        File.WriteAllText(path, "{}");
        Assert.Throws<InvalidDataException>(() => ProposalSupportRecords.Readback(plan, action, stage));
    }

    [Fact]
    public void RejectsReceiptAndActionOutsidePlan()
    {
        var receipt = Action(SupportContent.ReceiptAfterVerification);
        Assert.Throws<InvalidDataException>(() => ProposalSupportRecords.Write(plan, receipt, stage));
        Assert.Throws<InvalidDataException>(() => ProposalSupportRecords.Readback(plan, receipt, stage));
        var model = Assert.Single(plan.Actions.Where(a => a.Id == "20:model/BaseModel"));
        Assert.Throws<InvalidDataException>(() => ProposalSupportRecords.Write(plan, model, stage));

        var foreignPlan = Assert.IsType<ProposalPlan>(ProposalPlanner.Build(
            plan.Inputs with { SiteName = "Foreign Site" }, standards).Plan);
        var foreignSupport = Assert.Single(foreignPlan.Actions.Where(a =>
            a.Data is SupportRecordData { Content: SupportContent.EmptyDecisionLog }));
        Assert.Throws<InvalidDataException>(() => ProposalSupportRecords.Write(plan, foreignSupport, stage));
        Assert.Throws<InvalidDataException>(() => ProposalSupportRecords.Readback(plan, foreignSupport, stage));
    }

    [Fact]
    public void WriteRejectsReparseStage()
    {
        string foreign = Path.Combine(root, "foreign");
        Directory.CreateDirectory(foreign);
        Directory.Delete(stage);
        Directory.CreateSymbolicLink(stage, foreign);
        try
        {
            Assert.Throws<ProposalConditionException>(() =>
                ProposalSupportRecords.Write(plan, Action(SupportContent.EmptyDecisionLog), stage));
            Assert.Empty(Directory.GetFileSystemEntries(foreign));
        }
        finally
        {
            Directory.Delete(stage);
            Directory.CreateDirectory(stage);
        }
    }

    private PlannedAction Action(SupportContent content) =>
        Assert.Single(plan.Actions.Where(a => a.Data is SupportRecordData support && support.Content == content));

    private string PathFor(PlannedAction action)
    {
        string path = ProposalFiles.Child(stage, action.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private string ConfigurationJson()
    {
        using var document = JsonDocument.Parse(plan.ToJson());
        return document.RootElement.GetProperty("configuration").GetRawText();
    }

    public void Dispose() => Directory.Delete(root, true);
}
