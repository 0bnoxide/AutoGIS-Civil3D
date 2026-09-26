using AutoGIS.Civil3D.Proposal;

namespace AutoGIS.Civil3D.Adapter;

public sealed class NativeProposalHost : IProposalHost
{
    private ProposalPlan? inspectedPlan;

    public PreflightReport Inspect(ProposalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        inspectedPlan = null;
        foreach (var action in plan.Actions)
        {
            if (!IsModelAction(action))
                return new([new(ExecutionIssueCodes.PreflightFailed,
                    $"Native proposal operation is not implemented: {action.Id} ({action.Operation}).", action.RelativePath)]);
        }
        inspectedPlan = plan;
        return new([]);
    }

    public void Apply(PlannedAction action, string stagingRoot)
    {
        if (!IsModelAction(action))
            throw new NotSupportedException($"Native proposal operation is not implemented: {action.Operation}.");
        var plan = inspectedPlan ?? throw new InvalidOperationException("Native plan inspection must pass before applying actions.");
        ValidateModelAction(plan, action, stagingRoot);
        switch (action.Operation)
        {
            case ProposalOperation.CreateFolder:
                if (action.RelativePath.Length > 0)
                    ProposalFiles.ReserveStage(ProposalFiles.Child(stagingRoot, action.RelativePath));
                break;
            case ProposalOperation.CreateModelDrawing:
                DrawingWriter.CreateModel(action, stagingRoot);
                break;
            case ProposalOperation.AddXref:
                DrawingWriter.AddModelOverlay(action, stagingRoot);
                break;
            default:
                throw new NotSupportedException($"Native proposal operation is not implemented: {action.Operation}.");
        }
    }

    // Writer and verifier close their side databases within each call.
    public void CloseCreatedArtifacts() { }

    public VerificationReport Verify(ProposalPlan plan, string artifactRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var refusal = Inspect(plan).Issues;
        if (!refusal.IsEmpty) return new([], refusal, [], []);
        return NativeProposalVerifier.VerifyModels(plan, artifactRoot);
    }

    internal static void ValidateModelAction(ProposalPlan plan, PlannedAction action, string stagingRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(action);
        if (!plan.Actions.Any(planned => ReferenceEquals(planned, action)))
            throw new InvalidDataException("Native action is not an element of the inspected plan.");
        if (!IsModelAction(action))
            throw new NotSupportedException($"Native proposal operation is not implemented: {action.Operation}.");
        if (action.RelativePath.Length > 0)
            _ = ProposalFiles.Child(stagingRoot, action.RelativePath);
        switch (action)
        {
            case { Operation: ProposalOperation.CreateFolder, Data: FolderData }:
                break;
            case { Operation: ProposalOperation.CreateModelDrawing, Data: ModelDrawingData model }:
                if (plan.Configuration.Standards.Models.Count(m => m.Role == model.Role && m.Path == action.RelativePath) != 1 ||
                    !string.Equals(model.Template, plan.Configuration.Standards.ModelTemplate, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model action does not match the inspected standards.");
                break;
            case { Operation: ProposalOperation.AddXref, Data: XrefData xref }:
                var host = plan.Configuration.Standards.Models.SingleOrDefault(m => m.Role == xref.Reference.HostRole);
                var target = plan.Configuration.Standards.Models.SingleOrDefault(m => m.Role == xref.Reference.ReferenceRole);
                if (host is null || target is null || action.RelativePath != host.Path || xref.Reference.Mode != "Overlay")
                    throw new InvalidDataException("Model overlay action does not match the inspected plan.");
                string expectedTarget = ProposalFiles.Child(stagingRoot, target.Path);
                string resolved = ResolveOverlayTarget(stagingRoot, action.RelativePath, xref.RelativeReferencePath);
                if (!string.Equals(resolved, expectedTarget, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model overlay target differs from its planned role.");
                break;
            default:
                throw new InvalidDataException("Native action operation and data do not match.");
        }
    }

    internal static string ResolveOverlayTarget(string stagingRoot, string hostRelativePath, string overlayRelativePath)
    {
        string host = ProposalFiles.Child(stagingRoot, hostRelativePath);
        if (string.IsNullOrWhiteSpace(overlayRelativePath) || Path.IsPathRooted(overlayRelativePath) ||
            overlayRelativePath.Contains(':'))
            throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, "Overlay path must be relative.");
        string target = ProposalFiles.Full(Path.Combine(Path.GetDirectoryName(host)!,
            overlayRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!ProposalFiles.Within(target, stagingRoot))
            throw new ProposalConditionException(ExecutionIssueCodes.UnsafeExecutionPath, "Overlay target escapes staging.");
        ProposalFiles.RejectReparseAncestors(target);
        return target;
    }

    private static bool IsModelAction(PlannedAction action) => action switch
    {
        { Operation: ProposalOperation.CreateFolder, Data: FolderData } => true,
        { Operation: ProposalOperation.CreateModelDrawing, Data: ModelDrawingData } => true,
        { Operation: ProposalOperation.AddXref, Data: XrefData { Reference: { HostRole: "ProposedDesignModel", Mode: "Overlay" } } } => true,
        _ => false
    };
}
