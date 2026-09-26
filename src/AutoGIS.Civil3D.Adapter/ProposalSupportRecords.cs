using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoGIS.Civil3D.Proposal;

namespace AutoGIS.Civil3D.Adapter;

internal static class ProposalSupportRecords
{
    internal static void Write(ProposalPlan plan, PlannedAction action, string stage)
    {
        string path = Path(plan, action, stage);
        byte[] expected = ExpectedBytes(plan, (SupportRecordData)action.Data);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(expected);
        file.Flush(true);
    }

    internal static string Readback(ProposalPlan plan, PlannedAction action, string stage)
    {
        string path = Path(plan, action, stage);
        byte[] actual = File.ReadAllBytes(path);
        if (!actual.AsSpan().SequenceEqual(ExpectedBytes(plan, (SupportRecordData)action.Data)))
            throw new InvalidDataException($"Support record differs from the approved plan: {action.RelativePath}");
        return Convert.ToHexString(SHA256.HashData(actual));
    }

    private static string Path(ProposalPlan plan, PlannedAction action, string stage)
    {
        if (!plan.Actions.Contains(action) || action.Operation != ProposalOperation.WriteSupportRecord ||
            action.Data is not SupportRecordData { Content: not SupportContent.ReceiptAfterVerification })
            throw new InvalidDataException("Action is not a planned pre-receipt support record.");
        string path = ProposalFiles.Child(stage, action.RelativePath);
        ProposalFiles.RejectReparseAncestors(path);
        return path;
    }

    private static byte[] ExpectedBytes(ProposalPlan plan, SupportRecordData data)
    {
        string json = data.Content switch
        {
            SupportContent.ProjectConfiguration => ConfigurationJson(plan),
            SupportContent.EmptySourceRegister or SupportContent.EmptyAssumptionsLog or SupportContent.EmptyDecisionLog => "[]",
            SupportContent.CreationPlan => plan.ToJson(),
            _ => throw new InvalidDataException("Unsupported support record content.")
        };
        return Encoding.UTF8.GetBytes(json);
    }

    private static string ConfigurationJson(ProposalPlan plan)
    {
        using var document = JsonDocument.Parse(plan.ToJson());
        return document.RootElement.GetProperty("configuration").GetRawText();
    }
}
