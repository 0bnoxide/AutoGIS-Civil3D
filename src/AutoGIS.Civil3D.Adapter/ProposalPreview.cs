using AutoGIS.Civil3D.Proposal;
using System.Globalization;

namespace AutoGIS.Civil3D.Adapter;

public static class ProposalPreview
{
    public static void Write(ProposalPlan plan, TextWriter output)
    {
        output.WriteLine($"Final target: {plan.FinalRoot}");
        output.WriteLine($"Standards manifest version: {plan.ManifestVersion}");
        output.WriteLine("Execution unavailable in preview build");
        output.WriteLine("Existing ground: pending; no surface is created.");
        output.WriteLine("Data shortcuts: folder only; associate the project and publish a real surface manually.");
        output.WriteLine("Viewports: placeholders only; framing and scale selection remain manual.");
        output.WriteLine("Native template contents and Sheet Set operations still require Civil 3D qualification.");
        output.WriteLine();
        output.WriteLine("Inputs:");
        var inputs = plan.Inputs;
        foreach (var (name, value) in new[] {
            ("ClientName", inputs.ClientName), ("SiteName", inputs.SiteName),
            ("ProposalYear", inputs.ProposalYear.ToString(CultureInfo.InvariantCulture)),
            ("Orientation", inputs.Orientation), ("SheetSize", inputs.SheetSize),
            ("ClientNumber", inputs.ClientNumber), ("ProjectNumber", inputs.ProjectNumber),
            ("ProposalNumber", inputs.ProposalNumber), ("SiteAddress", inputs.SiteAddress),
            ("ProjectManager", inputs.ProjectManager) })
            output.WriteLine($"  {InputName(name)}: {Value(value)}");
        output.WriteLine();
        output.WriteLine("Actions:");
        foreach (var action in plan.Actions)
        {
            WriteAction(action, output);
            output.WriteLine();
        }
    }

    private static void WriteAction(PlannedAction action, TextWriter output)
    {
        void Heading(string description) => output.WriteLine($"[{action.Id}] {description}: {action.RelativePath}");
        void Detail(string description) => output.WriteLine($"  {description}");
        switch (action.Data)
        {
            case FolderData folder:
                Heading("Create folder");
                if (folder.IsDataShortcutFolder) Detail("Data-shortcut directory; project association remains manual.");
                break;
            case ModelDrawingData model:
                Heading("Create model drawing");
                Detail($"{Role(model.Role)}; template: {model.Template}");
                break;
            case SheetDrawingData sheet:
                Heading("Create sheet drawing");
                Detail($"Sheet {sheet.Sheet.Number}: {sheet.Sheet.Title} ({Role(sheet.Sheet.Role)})");
                var profile = sheet.Profile;
                Detail($"Template: {profile.Template}; {profile.Orientation}, {profile.Size}");
                Detail($"Layout: {profile.Layout}; title block: {profile.TitleBlock}; Page setup: {profile.PageSetup}");
                var plot = profile.Plot;
                Detail(FormattableString.Invariant($"Plot: {plot.Device}; media {plot.Media}; style {plot.StyleSheet}; units {plot.Units}; scale {plot.Scale}; rotation {plot.Rotation}"));
                Detail($"Printable area: {Area(profile.PrintableArea)}");
                foreach (var placeholder in profile.Placeholders.Where(p => p.SheetRole == sheet.Sheet.Role))
                    Detail($"Placeholder {placeholder.Label} ({placeholder.Kind}): {Area(placeholder.Rectangle)}");
                break;
            case SheetSetData:
                Heading("Create sheet set");
                break;
            case SheetSetPropertiesData properties:
                Heading("Set sheet set properties");
                foreach (var property in properties.Properties)
                    Detail($"{InputName(property.Input)} → {property.DstProperty}: {Value(property.Value)}");
                break;
            case SheetRegistrationData registration:
                Heading($"Register sheet {registration.Sheet.Number}");
                Detail($"{registration.Sheet.Title} ({Role(registration.Sheet.Role)}); layout: {registration.Layout}");
                break;
            case TitleBlockData titleBlock:
                Heading($"Bind title block {titleBlock.BlockName}");
                Detail($"Layout: {titleBlock.Layout}; sheet set: {titleBlock.SheetSetPath}; DST property → title-block attribute:");
                foreach (var mapping in titleBlock.Mappings)
                    Detail($"{InputName(mapping.Input)}: {mapping.DstProperty} → {mapping.TitleBlockAttribute}");
                break;
            case XrefData xref:
                Heading($"Add {xref.Reference.Mode} Xref");
                Detail($"{Role(xref.Reference.HostRole)} ← {Role(xref.Reference.ReferenceRole)}; Reference: {xref.RelativeReferencePath}");
                var reference = xref.Reference;
                Detail(FormattableString.Invariant($"Insertion: ({reference.Insertion.X}, {reference.Insertion.Y}, {reference.Insertion.Z}); scale {reference.Scale}; rotation {reference.Rotation}"));
                break;
            case SupportRecordData support:
                Heading(support.Content switch
                {
                    SupportContent.ProjectConfiguration => "Write project configuration",
                    SupportContent.EmptySourceRegister => "Create empty source register",
                    SupportContent.EmptyAssumptionsLog => "Create empty assumptions log",
                    SupportContent.EmptyDecisionLog => "Create empty decision log",
                    SupportContent.CreationPlan => "Write creation plan",
                    SupportContent.ReceiptAfterVerification => "Write creation receipt after verification",
                    _ => throw new InvalidOperationException("Unsupported support record in preview.")
                });
                break;
            default:
                throw new InvalidOperationException("Unsupported action in preview.");
        }
    }

    private static string Value(string? value) => value ?? "(not supplied)";
    private static string Area(Proposal.Rectangle area) =>
        FormattableString.Invariant($"x {area.X}, y {area.Y}, width {area.Width}, height {area.Height}");

    private static string InputName(string input) => input switch
    {
        "ClientName" => "Client name",
        "SiteName" => "Site name",
        "ProposalYear" => "Proposal year",
        "Orientation" => "Orientation",
        "SheetSize" => "Sheet size",
        "ClientNumber" => "Client number",
        "ProjectNumber" => "Official project number",
        "ProposalNumber" => "Proposal number / identifier",
        "SiteAddress" => "Site address",
        "ProjectManager" => "Project manager",
        _ => throw new InvalidOperationException("Unsupported input in preview.")
    };

    private static string Role(string role) => role switch
    {
        "BaseModel" => "Base model",
        "ExistingConditionsModel" => "Existing conditions model",
        "ProposedDesignModel" => "Proposed design model",
        "SiteOverviewSheet" => "Site overview sheet",
        "ExistingConditionsSheet" => "Existing conditions sheet",
        "ProposedSitePlanSheet" => "Proposed site plan sheet",
        _ => throw new InvalidOperationException("Unsupported drawing role in preview.")
    };
}
