using System.Globalization;
using AutoGIS.Civil3D.Proposal;

namespace AutoGIS.Civil3D.Adapter;

internal sealed class NewProposalForm : Form
{
    public ProposalApproval? ApprovedPlan { get; private set; }
    private readonly Dictionary<string, TextBox> fields = new();
    private readonly TextBox previewText = new() { Name = "PreviewText", Multiline = true, ReadOnly = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly Button approve = new() { Name = "Approve", Text = "Approve preview", AutoSize = true, Enabled = false };
    private readonly Label status = new() { AutoSize = true, Text = "Select a standards manifest and enter the proposal inputs." };
    private readonly System.Windows.Forms.Timer dependencyCheck = new() { Interval = 2000 };
    private ProposalPreviewSession? preview;
    private bool dependencyCheckBusy;

    public NewProposalForm()
    {
        Text = "AutoGIS — New Proposal preview";
        Size = new Size(1120, 800);
        MinimumSize = new Size(900, 650);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var manifestRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        manifestRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        manifestRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        manifestRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        manifestRow.Controls.Add(new Label { Text = "Standards manifest", AutoSize = true, Anchor = AnchorStyles.Left });
        manifestRow.Controls.Add(Input("ManifestPath"));
        var browse = new Button { Text = "Browse…", AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Standards manifest (*.json)|*.json", CheckFileExists = true, Title = "Select approved company standards" };
            if (dialog.ShowDialog(this) == DialogResult.OK) fields["ManifestPath"].Text = dialog.FileName;
        };
        manifestRow.Controls.Add(browse);
        layout.Controls.Add(manifestRow);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var inputTable = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, Padding = new Padding(0, 0, 12, 0) };
        foreach (var (name, label) in new[] {
            ("ClientName", "Client name *"), ("SiteName", "Site name *"),
            ("ProposalYear", "Proposal year *"), ("Orientation", "Orientation * (from manifest)"),
            ("SheetSize", "Sheet size * (from manifest)"), ("ClientNumber", "Client number (optional)"),
            ("ProjectNumber", "Official project number (optional)"), ("ProposalNumber", "Proposal number / identifier (optional)"),
            ("SiteAddress", "Site address (optional)"), ("ProjectManager", "Project manager (optional)") })
        {
            inputTable.Controls.Add(new Label { Text = label, AutoSize = true });
            inputTable.Controls.Add(Input(name));
        }
        body.Controls.Add(inputTable);
        body.Controls.Add(previewText);
        layout.Controls.Add(body);
        layout.Controls.Add(status);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var build = new Button { Name = "Preview", Text = "Build preview", AutoSize = true };
        build.Click += (_, _) => BuildPreview();
        approve.Click += (_, _) => ApprovePreview();
        var execute = new Button { Name = "Execute", Text = "Create proposal", Enabled = false, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        CancelButton = cancel;
        buttons.Controls.AddRange([build, approve, execute, cancel,
            new Label { Text = "Execution unavailable in preview build", AutoSize = true, Margin = new Padding(10, 8, 0, 0) }]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        dependencyCheck.Tick += async (_, _) => await CheckDependenciesAsync();
        FormClosed += (_, _) => InvalidatePreview("Cancelled.");
    }

    private async Task CheckDependenciesAsync()
    {
        if (dependencyCheckBusy || preview is null)
            return;
        dependencyCheckBusy = true;
        ProposalPreviewSession checkedPreview = preview;
        ProposalInputs inputs = ReadInputs();
        string manifestPath = fields["ManifestPath"].Text;
        try
        {
            bool current = await Task.Run(() => checkedPreview.IsCurrent(inputs, manifestPath));
            if (!current && !IsDisposed && !Disposing && ReferenceEquals(preview, checkedPreview))
                InvalidatePreview("Standards or templates changed. Build a new preview.");
        }
        finally
        {
            dependencyCheckBusy = false;
        }
    }

    private TextBox Input(string name)
    {
        var input = new TextBox { Name = name, Dock = DockStyle.Top, AccessibleName = name };
        fields.Add(name, input);
        input.TextChanged += (_, _) => InvalidatePreview("Inputs changed. Build a new preview.");
        return input;
    }

    private ProposalInputs ReadInputs()
    {
        string Value(string name) => fields[name].Text;
        string? Optional(string name) => Value(name).Length == 0 ? null : Value(name);
        int.TryParse(Value("ProposalYear"), NumberStyles.None, CultureInfo.InvariantCulture, out int year);
        return new(Value("ClientName"), Value("SiteName"), year, Value("Orientation"), Value("SheetSize"),
            Optional("ClientNumber"), Optional("ProjectNumber"), Optional("ProposalNumber"), Optional("SiteAddress"), Optional("ProjectManager"));
    }

    private void BuildPreview()
    {
        InvalidatePreview("Building preview…");
        try
        {
            preview = ProposalPreviewSession.Create(ReadInputs(), fields["ManifestPath"].Text);
            using var output = new StringWriter(CultureInfo.InvariantCulture);
            ProposalPreview.Write(preview.Plan, output);
            previewText.Text = output.ToString();
            approve.Enabled = preview.IsCurrent(ReadInputs(), fields["ManifestPath"].Text);
            status.Text = approve.Enabled ? "Review the plan, then approve this preview." : "Dependencies changed. Build a new preview.";
            dependencyCheck.Start();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            InvalidatePreview("Preview unavailable. Correct the inputs or selected files.");
            previewText.Text = ex.Message;
        }
    }

    private void ApprovePreview()
    {
        ApprovedPlan = preview?.TryApprove(ReadInputs(), fields["ManifestPath"].Text);
        if (ApprovedPlan is null)
            InvalidatePreview("Preview changed. Build a new preview before approval.");
        else
            status.Text = "Preview approved. Execution unavailable in preview build.";
    }

    private void InvalidatePreview(string message)
    {
        dependencyCheck.Stop();
        preview?.Invalidate();
        preview = null;
        ApprovedPlan = null;
        approve.Enabled = false;
        previewText.Clear();
        status.Text = message;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            dependencyCheck.Dispose();
            preview?.Invalidate();
            ApprovedPlan = null;
        }
        base.Dispose(disposing);
    }
}
