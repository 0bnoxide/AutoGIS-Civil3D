using System.Collections.Immutable;
using System.Globalization;

namespace AutoGIS.Civil3D.Proposal;

public static class ProposalPlanner
{
    public static PlanResult Build(ProposalInputs inputs, StandardsManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(manifest);
        string name = inputs.ClientName + " - " + inputs.SiteName;
        if (!WindowsPaths.IsComponent(inputs.ClientName) || !WindowsPaths.IsComponent(inputs.SiteName) ||
            !WindowsPaths.IsComponent(name) || inputs.ProposalYear is < 1 or > 9999 ||
            !TextValues.IsValid(inputs.Orientation) || !TextValues.IsValid(inputs.SheetSize) ||
            new[] { inputs.ClientNumber, inputs.ProjectNumber, inputs.ProposalNumber, inputs.SiteAddress, inputs.ProjectManager }
                .Any(v => v is not null && !TextValues.IsValid(v)))
            return new(null, [new(ProposalIssueCodes.InvalidInputs, "Supply safe client/site names, a valid year, sheet selections, and nonblank optional values.")]);
        var root = manifest.BaseRoots.SingleOrDefault(r => r.Year == inputs.ProposalYear);
        if (root is null)
            return new(null, [new(ProposalIssueCodes.UnsupportedYear, "The manifest has no root for the selected proposal year.", "ProposalYear")]);
        var profile = manifest.Profiles.SingleOrDefault(p => p.Orientation == inputs.Orientation && p.Size == inputs.SheetSize);
        if (profile is null)
            return new(null, [new(ProposalIssueCodes.UnsupportedSheet, "Select a supported orientation and sheet size.")]);

        var actions = new List<PlannedAction>();
        void Add(string id, ProposalOperation operation, string path, IEnumerable<string> dependencies, PlannedActionData data) =>
            actions.Add(new(id, operation, path, dependencies, data));
        string FolderId(string path) => path.Length == 0 ? "00:root" : "10:folder/" +
            (path.Count(c => c == '/') + 1).ToString("D5", CultureInfo.InvariantCulture) + "/" +
            manifest.Folders.Single(f => f.Equals(path, StringComparison.OrdinalIgnoreCase));
        string ParentId(string path) => FolderId(WindowsPaths.Parent(path));
        Add("00:root", ProposalOperation.CreateFolder, "", [], new FolderData(false));
        foreach (string folder in manifest.Folders)
            Add(FolderId(folder), ProposalOperation.CreateFolder, folder, [ParentId(folder)],
                new FolderData(folder.Equals(manifest.DataShortcutPath, StringComparison.OrdinalIgnoreCase)));
        foreach (var model in manifest.Models)
            Add("20:model/" + model.Role, ProposalOperation.CreateModelDrawing, model.Path, [ParentId(model.Path)], new ModelDrawingData(model.Role, manifest.ModelTemplate));
        foreach (var sheet in manifest.Sheets)
            Add("30:sheet/" + sheet.Role, ProposalOperation.CreateSheetDrawing, sheet.Path, [ParentId(sheet.Path)],
                new SheetDrawingData(sheet, profile with { Placeholders = profile.Placeholders.Where(p => p.SheetRole == sheet.Role).ToImmutableArray() }));
        Add("40:sheet-set", ProposalOperation.CreateSheetSet, manifest.SheetSetPath, [ParentId(manifest.SheetSetPath)], new SheetSetData());
        var properties = manifest.PropertyMappings.Select(p => new PropertyValue(p.Input, p.DstProperty, InputValue(inputs, p.Input))).ToImmutableArray();
        Add("50:properties", ProposalOperation.SetSheetSetProperties, manifest.SheetSetPath, ["40:sheet-set"], new SheetSetPropertiesData(properties));
        foreach (var sheet in manifest.Sheets)
        {
            Add("60:register/" + sheet.Role, ProposalOperation.RegisterSheet, manifest.SheetSetPath,
                ["40:sheet-set", "50:properties", "30:sheet/" + sheet.Role], new SheetRegistrationData(sheet, profile.Layout));
            Add("65:title-block/" + sheet.Role, ProposalOperation.BindTitleBlock, sheet.Path,
                ["30:sheet/" + sheet.Role, "50:properties", "60:register/" + sheet.Role],
                new TitleBlockData(manifest.SheetSetPath, profile.Layout, profile.TitleBlock, manifest.PropertyMappings));
        }
        var drawings = manifest.Models.Select(m => (m.Role, m.Path, Id: "20:model/" + m.Role))
            .Concat(manifest.Sheets.Select(s => (s.Role, s.Path, Id: "30:sheet/" + s.Role))).ToDictionary(d => d.Role, StringComparer.Ordinal);
        foreach (var xref in manifest.Xrefs)
        {
            var host = drawings[xref.HostRole];
            var reference = drawings[xref.ReferenceRole];
            Add("70:xref/" + xref.HostRole + "/" + xref.ReferenceRole, ProposalOperation.AddXref, host.Path,
                [host.Id, reference.Id], new XrefData(xref, RelativeReference(host.Path, reference.Path)));
        }
        var nativeIds = actions.Select(a => a.Id).ToArray();
        foreach (var support in manifest.SupportRecords.Where(s => s.Role != "CreationReceipt"))
            Add("80:support/" + support.Role, ProposalOperation.WriteSupportRecord, support.Path,
                nativeIds.Append(ParentId(support.Path)), new SupportRecordData(Support(support.Role)));
        var receipt = manifest.SupportRecords.Single(s => s.Role == "CreationReceipt");
        Add("90:receipt", ProposalOperation.WriteSupportRecord, receipt.Path,
            actions.Select(a => a.Id).ToArray(), new SupportRecordData(SupportContent.ReceiptAfterVerification));
        string finalRoot = root.Path.TrimEnd('/') + "/" + name;
        if (actions.Any(a => finalRoot.Length + (a.RelativePath.Length == 0 ? 0 : a.RelativePath.Length + 1) >= 32767))
            return new(null, [new(ProposalIssueCodes.UnsafePath, "A planned output exceeds the Windows extended path limit.")]);
        return new(new ProposalPlan(inputs, manifest, root.Path, name, actions), []);
    }

    private static string? InputValue(ProposalInputs inputs, string field) => field switch
    {
        "ClientName" => inputs.ClientName,
        "SiteName" => inputs.SiteName,
        "ProposalYear" => inputs.ProposalYear.ToString(CultureInfo.InvariantCulture),
        "Orientation" => inputs.Orientation,
        "SheetSize" => inputs.SheetSize,
        "ClientNumber" => inputs.ClientNumber,
        "ProjectNumber" => inputs.ProjectNumber,
        "ProposalNumber" => inputs.ProposalNumber,
        "SiteAddress" => inputs.SiteAddress,
        "ProjectManager" => inputs.ProjectManager,
        _ => throw new InvalidOperationException("Unvalidated metadata mapping.")
    };

    private static SupportContent Support(string role) => role switch
    {
        "ProjectConfiguration" => SupportContent.ProjectConfiguration,
        "SourceRegister" => SupportContent.EmptySourceRegister,
        "AssumptionsLog" => SupportContent.EmptyAssumptionsLog,
        "DecisionLog" => SupportContent.EmptyDecisionLog,
        "CreationPlan" => SupportContent.CreationPlan,
        _ => throw new InvalidOperationException("Unvalidated support role.")
    };

    private static string RelativeReference(string host, string reference)
    {
        string parent = WindowsPaths.Parent(host);
        string[] from = parent.Length == 0 ? [] : parent.Split('/');
        string[] to = reference.Split('/');
        int common = 0;
        while (common < from.Length && common < to.Length && from[common].Equals(to[common], StringComparison.OrdinalIgnoreCase)) common++;
        return string.Join("/", Enumerable.Repeat("..", from.Length - common).Concat(to.Skip(common)));
    }
}
