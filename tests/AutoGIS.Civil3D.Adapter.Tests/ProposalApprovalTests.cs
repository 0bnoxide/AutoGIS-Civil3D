using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AutoGIS.Civil3D.Proposal;
using Xunit;

namespace AutoGIS.Civil3D.Adapter.Tests;

public sealed class ProposalApprovalTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "AutoGIS-preview-tests", Guid.NewGuid().ToString("N"));
    private readonly ProposalInputs inputs = new("Synthetic Client", "Synthetic Site", 2026, "Landscape", "TEST-A1");
    private string ManifestPath => Path.Combine(root, "standards.json");

    public ProposalApprovalTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Model.dwt"), "synthetic model bytes");
        File.WriteAllText(Path.Combine(root, "Sheet.dwt"), "synthetic sheet bytes");
        var json = JsonNode.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "synthetic-standards.json")))!;
        json["baseRoots"]![0]!["path"] = root.Replace('\\', '/') + "/Proposals";
        json["modelTemplate"] = Path.Combine(root, "Model.dwt").Replace('\\', '/');
        json["profiles"]![0]!["template"] = Path.Combine(root, "Sheet.dwt").Replace('\\', '/');
        File.WriteAllText(ManifestPath, json.ToJsonString());
    }

    [Fact]
    public void SnapshotDefensivelyCopiesSourceAndExposesReadOnlyFingerprints()
    {
        var source = new Dictionary<string, string> { ["manifest.json"] = "original" };
        var approval = new ProposalApproval("canonical plan", source);
        source["manifest.json"] = "changed";
        source["another"] = "added";
        Assert.Equal("original", approval.DependencyFingerprints["manifest.json"]);
        Assert.Single(approval.DependencyFingerprints);
        if (approval.DependencyFingerprints is IDictionary<string, string> mutable)
            Assert.Throws<NotSupportedException>(() => mutable["manifest.json"] = "changed");
    }

    [Fact]
    public void PreviewCapturesExactManifestAndSelectedTemplateBytesWithoutWritingArtifacts()
    {
        var before = SnapshotFiles();
        var preview = ProposalPreviewSession.Create(inputs, ManifestPath);
        var approval = Assert.IsType<ProposalApproval>(preview.TryApprove(inputs, ManifestPath));
        Assert.Equal(preview.Plan.ToJson(), approval.PlanJson);
        Assert.Equal(3, approval.DependencyFingerprints.Count);
        foreach (string path in new[] { ManifestPath, Path.Combine(root, "Model.dwt"), Path.Combine(root, "Sheet.dwt") })
            Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), approval.DependencyFingerprints[path]);
        Assert.Same(approval, preview.TryApprove(inputs, ManifestPath));
        Assert.Equal(before, SnapshotFiles());
        Assert.False(Directory.Exists(preview.Plan.FinalRoot));
    }

    [Theory]
    [InlineData("standards.json", false)]
    [InlineData("Model.dwt", false)]
    [InlineData("Sheet.dwt", false)]
    [InlineData("standards.json", true)]
    [InlineData("Model.dwt", true)]
    [InlineData("Sheet.dwt", true)]
    public void ReplacedOrDeletedDependencyRefusesApprovalAndCannotRecaptureExpectations(string name, bool delete)
    {
        var preview = ProposalPreviewSession.Create(inputs, ManifestPath);
        string path = Path.Combine(root, name);
        byte[] original = File.ReadAllBytes(path);
        File.Delete(path);
        if (!delete) File.WriteAllText(path, "replacement after preview");
        var before = SnapshotFiles();
        Assert.Null(preview.TryApprove(inputs, ManifestPath));
        Assert.Equal(before, SnapshotFiles());
        File.WriteAllBytes(path, original);
        Assert.Null(preview.TryApprove(inputs, ManifestPath));
    }

    [Fact]
    public void EditedInputsOrManifestSelectionPermanentlyInvalidatePreview()
    {
        var preview = ProposalPreviewSession.Create(inputs, ManifestPath);
        Assert.Null(preview.TryApprove(inputs with { SiteName = "Another site" }, ManifestPath));
        Assert.Null(preview.TryApprove(inputs, ManifestPath));
        preview = ProposalPreviewSession.Create(inputs, ManifestPath);
        string alternate = Path.Combine(root, "another.json");
        File.Copy(ManifestPath, alternate);
        Assert.Null(preview.TryApprove(inputs, alternate));
        Assert.Null(preview.TryApprove(inputs, ManifestPath));
    }

    [Fact]
    public void CancelInvalidatesWithoutWritingAnything()
    {
        var before = SnapshotFiles();
        var preview = ProposalPreviewSession.Create(inputs, ManifestPath);
        preview.Invalidate();
        Assert.Null(preview.TryApprove(inputs, ManifestPath));
        Assert.Equal(before, SnapshotFiles());
    }

    [Fact]
    public void InvalidInputsAndMissingTemplatesFailBeforeAnyArtifacts()
    {
        var before = SnapshotFiles();
        Assert.Throws<InvalidDataException>(() => ProposalPreviewSession.Create(inputs with { ClientName = "" }, ManifestPath));
        Assert.Equal(before, SnapshotFiles());
        File.Delete(Path.Combine(root, "Sheet.dwt"));
        before = SnapshotFiles();
        Assert.Throws<FileNotFoundException>(() => ProposalPreviewSession.Create(inputs, ManifestPath));
        Assert.Equal(before, SnapshotFiles());
    }

    private string[] SnapshotFiles() => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(path => path + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();

    [Fact]
    public void FormRequiresExplicitInputsAndInvalidatesApprovalOnEditAndCancel()
    {
        Exception? failure = null;
        var before = SnapshotFiles();
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new NewProposalForm();
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-20000, -20000);
                form.Show();
                T Control<T>(string name) where T : Control => Assert.IsType<T>(Assert.Single(form.Controls.Find(name, true)));
                var preview = Control<Button>("Preview");
                var approve = Control<Button>("Approve");
                Assert.False(approve.Enabled);
                Assert.False(Control<Button>("Execute").Enabled);
                foreach (string name in new[] { "ManifestPath", "ClientName", "SiteName", "ProposalYear", "Orientation", "SheetSize" })
                    Assert.Empty(Control<TextBox>(name).Text);
                Control<TextBox>("ManifestPath").Text = ManifestPath;
                preview.PerformClick();
                Assert.False(approve.Enabled);
                Assert.Contains("PROPOSAL_INVALID_INPUTS", Control<TextBox>("PreviewText").Text, StringComparison.Ordinal);
                Control<TextBox>("ClientName").Text = inputs.ClientName;
                Control<TextBox>("SiteName").Text = inputs.SiteName;
                Control<TextBox>("ProposalYear").Text = "2026";
                Control<TextBox>("Orientation").Text = inputs.Orientation;
                Control<TextBox>("SheetSize").Text = inputs.SheetSize;
                preview.PerformClick();
                Assert.True(approve.Enabled);
                Assert.Contains("Final target:", Control<TextBox>("PreviewText").Text, StringComparison.Ordinal);
                approve.PerformClick();
                Assert.NotNull(form.ApprovedPlan);
                Assert.False(Control<Button>("Execute").Enabled);
                Control<TextBox>("SiteName").Text = "Edited site";
                Assert.Null(form.ApprovedPlan);
                Assert.False(approve.Enabled);
                preview.PerformClick();
                File.WriteAllText(Path.Combine(root, "Sheet.dwt"), "edited after preview");
                approve.PerformClick();
                Assert.Null(form.ApprovedPlan);
                Assert.False(approve.Enabled);
                preview.PerformClick();
                approve.PerformClick();
                Assert.NotNull(form.ApprovedPlan);
                form.Close();
                Assert.Null(form.ApprovedPlan);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        File.WriteAllText(Path.Combine(root, "Sheet.dwt"), "synthetic sheet bytes");
        Assert.Equal(before, SnapshotFiles());
        Assert.Equal(3, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.GetDirectories(root));
    }

    public void Dispose() => Directory.Delete(root, true);
}
