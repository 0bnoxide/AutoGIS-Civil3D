using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoGIS.Civil3D.Proposal;

public sealed record PlanResult(ProposalPlan? Plan, ImmutableArray<ProposalIssue> Issues);

public enum ProposalOperation
{
    CreateFolder, CreateModelDrawing, CreateSheetDrawing, CreateSheetSet,
    SetSheetSetProperties, RegisterSheet, BindTitleBlock, AddXref, WriteSupportRecord
}

public enum SupportContent { ProjectConfiguration, EmptySourceRegister, EmptyAssumptionsLog, EmptyDecisionLog, CreationPlan, ReceiptAfterVerification }
public enum ExistingGroundState { Pending }

[JsonDerivedType(typeof(FolderData), "folder")]
[JsonDerivedType(typeof(ModelDrawingData), "model")]
[JsonDerivedType(typeof(SheetDrawingData), "sheet")]
[JsonDerivedType(typeof(SheetSetData), "sheetSet")]
[JsonDerivedType(typeof(SheetSetPropertiesData), "properties")]
[JsonDerivedType(typeof(SheetRegistrationData), "registration")]
[JsonDerivedType(typeof(TitleBlockData), "titleBlock")]
[JsonDerivedType(typeof(XrefData), "xref")]
[JsonDerivedType(typeof(SupportRecordData), "support")]
public abstract record PlannedActionData
{
    private protected PlannedActionData() { }
}

public sealed record FolderData(bool IsDataShortcutFolder) : PlannedActionData;
public sealed record ModelDrawingData(string Role, string Template) : PlannedActionData;
public sealed record SheetDrawingData(SheetStandard Sheet, SheetProfile Profile) : PlannedActionData;
public sealed record SheetSetData() : PlannedActionData;
public sealed record PropertyValue(string Input, string DstProperty, string? Value);
public sealed record SheetSetPropertiesData(ImmutableArray<PropertyValue> Properties) : PlannedActionData;
public sealed record SheetRegistrationData(SheetStandard Sheet, string Layout) : PlannedActionData;
public sealed record TitleBlockData(string SheetSetPath, string Layout, string BlockName,
    ImmutableArray<PropertyMapping> Mappings) : PlannedActionData;
public sealed record XrefData(XrefStandard Reference, string RelativeReferencePath) : PlannedActionData;
public sealed record SupportRecordData(SupportContent Content) : PlannedActionData;

public sealed class PlannedAction
{
    public string Id { get; }
    public ProposalOperation Operation { get; }
    public string RelativePath { get; }
    public ImmutableArray<string> Dependencies { get; }
    public PlannedActionData Data { get; }

    internal PlannedAction(string id, ProposalOperation operation, string path, IEnumerable<string> dependencies, PlannedActionData data)
    {
        Id = id;
        Operation = operation;
        RelativePath = path;
        Dependencies = dependencies.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        Data = data;
    }
}

public sealed record ProposalConfiguration(string FinalRoot, ProposalInputs Inputs, StandardsManifest Standards,
    ExistingGroundState ExistingGround, ImmutableArray<XrefData> ExpectedReferences);

public sealed class ProposalPlan
{
    public ImmutableArray<string> FinalRootComponents { get; }
    public string FinalRoot { get; }
    public ProposalInputs Inputs { get; }
    public int ManifestVersion { get; }
    public ImmutableArray<PlannedAction> Actions { get; }
    public ProposalConfiguration Configuration { get; }

    internal ProposalPlan(ProposalInputs inputs, StandardsManifest manifest, string baseRoot, string name, IEnumerable<PlannedAction> actions)
    {
        Inputs = inputs;
        ManifestVersion = manifest.Version;
        FinalRootComponents = [baseRoot, name];
        FinalRoot = baseRoot.TrimEnd('/') + "/" + name;
        Actions = actions.OrderBy(a => a.Id, StringComparer.Ordinal).ToImmutableArray();
        Configuration = new(FinalRoot, Inputs, manifest, ExistingGroundState.Pending,
            Actions.Select(a => a.Data).OfType<XrefData>().ToImmutableArray());
    }

    public string ToJson() => JsonSerializer.Serialize(this, PlanJson.Options);
}
