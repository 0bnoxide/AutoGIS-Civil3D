using AutoGIS.Civil3D.Proposal;
using System.Text.Json;

namespace AutoGIS.Civil3D.Adapter;

public static class ProposalPreview
{
    private static readonly JsonSerializerOptions Details = new() { WriteIndented = true };

    public static void Write(ProposalPlan plan, TextWriter output)
    {
        output.WriteLine($"Final target: {plan.FinalRoot}");
        output.WriteLine($"Standards manifest version: {plan.ManifestVersion}");
        output.WriteLine("Inputs:");
        output.WriteLine(JsonSerializer.Serialize(plan.Inputs, Details));
        output.WriteLine("Actions:");
        foreach (var action in plan.Actions)
        {
            output.WriteLine($"[{action.Id}] {action.Operation}: {action.RelativePath}");
            output.WriteLine(JsonSerializer.Serialize(action.Data, action.Data.GetType(), Details));
        }
        output.WriteLine("Execution unavailable in preview build");
        output.WriteLine("Existing ground: pending; no surface is created.");
        output.WriteLine("Data shortcuts: folder only; associate the project and publish a real surface manually.");
        output.WriteLine("Viewports: placeholders only; framing and scale selection remain manual.");
        output.WriteLine("Native template contents and Sheet Set operations still require Civil 3D qualification.");
    }
}
