using System.Text.Json;
using AutoGIS.Civil3D.Proposal;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AutoGIS.Civil3D.Mcp;

public sealed class ProposalTools
{
    private readonly string? _configuredManifestPath;

    public ProposalTools(string? manifestPath) => _configuredManifestPath = manifestPath;

    internal const string InputSchemaJson = """
    {
      "type": "object",
      "properties": {
        "client_name": {"type": "string"},
        "site_name": {"type": "string"},
        "proposal_year": {"type": "integer"},
        "orientation": {"type": "string"},
        "sheet_size": {"type": "string"},
        "client_number": {"type": "string"},
        "project_number": {"type": "string"},
        "proposal_number": {"type": "string"},
        "site_address": {"type": "string"},
        "project_manager": {"type": "string"}
      },
      "required": ["client_name", "site_name", "proposal_year", "orientation", "sheet_size"],
      "additionalProperties": false
    }
    """;

    internal const string OutputSchemaJson = """
    {
      "type": "object",
      "properties": {
        "status": {"type": "string", "enum": ["AdvisoryPlan", "NoPlan"]},
        "advisoryOnly": {"type": "boolean"},
        "nativePreflightPerformed": {"type": "boolean"},
        "templatesChecked": {"type": "boolean"},
        "manifestVersion": {"type": ["integer", "null"]},
        "proposedRootName": {"type": ["string", "null"]},
        "existingGround": {"type": ["string", "null"]},
        "actionCount": {"type": "integer"},
        "actions": {"type": "array", "items": {
          "type": "object",
          "properties": {
            "id": {"type": "string"},
            "operation": {"type": "string"},
            "relativePath": {"type": "string"},
            "dependencies": {"type": "array", "items": {"type": "string"}}
          },
          "required": ["id", "operation", "relativePath", "dependencies"],
          "additionalProperties": false
        }},
        "issues": {"type": "array", "items": {
          "type": "object",
          "properties": {
            "code": {"type": "string"},
            "explanation": {"type": "string"}
          },
          "required": ["code", "explanation"],
          "additionalProperties": false
        }}
      },
      "required": ["status", "advisoryOnly", "nativePreflightPerformed",
        "templatesChecked", "manifestVersion", "proposedRootName", "existingGround",
        "actionCount", "actions", "issues"],
      "additionalProperties": false
    }
    """;

    private static readonly HashSet<string> InputKeys = new(StringComparer.Ordinal)
    {
        "client_name", "site_name", "proposal_year", "orientation", "sheet_size",
        "client_number", "project_number", "proposal_number", "site_address",
        "project_manager"
    };

    public CallToolResult PreviewProposal(
        RequestContext<CallToolRequestParams> context, CancellationToken cancellationToken)
    {
        if (!TryReadInputs(context.Params.Arguments, out _))
            return Error("INVALID_ARGUMENTS");
        return Error("MANIFEST_NOT_CONFIGURED");
    }

    private static bool TryReadInputs(IDictionary<string, JsonElement>? args,
        out ProposalInputs? inputs)
    {
        inputs = null;
        if (args is null || args.Keys.Any(key => !InputKeys.Contains(key)) ||
            !ReadString(args, "client_name", true, out string? client) ||
            !ReadString(args, "site_name", true, out string? site) ||
            !ReadString(args, "orientation", true, out string? orientation) ||
            !ReadString(args, "sheet_size", true, out string? size) ||
            !ReadString(args, "client_number", false, out string? clientNumber) ||
            !ReadString(args, "project_number", false, out string? projectNumber) ||
            !ReadString(args, "proposal_number", false, out string? proposalNumber) ||
            !ReadString(args, "site_address", false, out string? address) ||
            !ReadString(args, "project_manager", false, out string? manager) ||
            !args.TryGetValue("proposal_year", out JsonElement rawYear) ||
            rawYear.ValueKind != JsonValueKind.Number ||
            !rawYear.TryGetInt32(out int year))
            return false;
        inputs = new(client!, site!, year, orientation!, size!,
            clientNumber, projectNumber, proposalNumber, address, manager);
        return true;
    }

    private static bool ReadString(IDictionary<string, JsonElement> args,
        string key, bool required, out string? value)
    {
        value = null;
        if (!args.TryGetValue(key, out JsonElement raw))
            return !required;
        if (raw.ValueKind != JsonValueKind.String)
            return false;
        value = raw.GetString();
        return value is not null && value.Length <= 1024;
    }

    private static CallToolResult Error(string code) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = code }] };
}

public sealed record PreviewAction(string Id, string Operation, string RelativePath,
    IReadOnlyList<string> Dependencies);
public sealed record PreviewIssue(string Code, string Explanation);
public sealed record ProposalPreviewOutput(string Status, bool AdvisoryOnly,
    bool NativePreflightPerformed, bool TemplatesChecked, int? ManifestVersion,
    string? ProposedRootName, string? ExistingGround, int ActionCount,
    IReadOnlyList<PreviewAction> Actions, IReadOnlyList<PreviewIssue> Issues);
