using System.Text.Json;
using AutoGIS.Civil3D.Proposal;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AutoGIS.Civil3D.Mcp;

public sealed class ProposalTools
{
    private const int MaxManifestBytes = 256 * 1024;
    private readonly StandardsManifest? _manifest;
    private readonly string? _manifestError;

    public ProposalTools(string? manifestPath)
    {
        if (!IsLocalJsonPath(manifestPath))
        {
            _manifestError = "MANIFEST_NOT_CONFIGURED";
            return;
        }
        try
        {
            using var stream = new FileStream(manifestPath!, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] bytes = new byte[MaxManifestBytes + 1];
            int used = 0;
            while (used < bytes.Length)
            {
                int read = stream.Read(bytes, used, bytes.Length - used);
                if (read == 0) break;
                used += read;
            }
            if (used > MaxManifestBytes)
            {
                _manifestError = "MANIFEST_TOO_LARGE";
                return;
            }
            ManifestResult parsed = StandardsManifest.Parse(bytes.AsSpan(0, used));
            _manifest = parsed.Manifest;
            _manifestError = parsed.Manifest is null ? "MANIFEST_INVALID" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            _manifestError = "MANIFEST_UNREADABLE";
        }
    }

    private static bool IsLocalJsonPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
            return false;
        try
        {
            return Path.IsPathFullyQualified(path) &&
                string.Equals(Path.GetExtension(path), ".json",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

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
        if (_manifestError is { } code)
            return Error(code);
        if (!TryReadInputs(context.Params.Arguments, out ProposalInputs? inputs))
            return Error("INVALID_ARGUMENTS");
        try
        {
            return FormatResult(ProposalPlanner.Build(inputs!, _manifest!));
        }
        catch (Exception)
        {
            return Error("PREVIEW_FAILED");
        }
    }

    private static CallToolResult FormatResult(PlanResult result)
    {
        ProposalPlan? plan = result.Plan;
        PreviewAction[] actions = plan is null ? [] :
            plan.Actions.Select(action => new PreviewAction(action.Id,
                action.Operation.ToString(), action.RelativePath,
                action.Dependencies.ToArray())).ToArray();
        PreviewIssue[] issues = plan is null
            ? result.Issues.Select(SafeIssue).ToArray() : [];
        var output = new ProposalPreviewOutput(
            plan is null ? "NoPlan" : "AdvisoryPlan", true, false, false,
            plan?.ManifestVersion, plan is null ? null : plan.FinalRootComponents[1],
            plan is null ? null : ExistingGroundState.Pending.ToString(),
            actions.Length, actions, issues);
        JsonElement body = JsonSerializer.SerializeToElement(output,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (JsonSerializer.SerializeToUtf8Bytes(body).Length > 64 * 1024)
            return Error("PREVIEW_TOO_LARGE");
        return new CallToolResult { Content = [], StructuredContent = body };
    }

    private static PreviewIssue SafeIssue(ProposalIssue issue) => issue.Code switch
    {
        ProposalIssueCodes.InvalidInputs =>
            new(issue.Code, "Check proposal input values."),
        ProposalIssueCodes.UnsupportedYear =>
            new(issue.Code, "Choose a year in configured standards."),
        ProposalIssueCodes.UnsupportedSheet =>
            new(issue.Code, "Choose a configured sheet profile."),
        ProposalIssueCodes.UnsafePath =>
            new(issue.Code, "A planned path is too long."),
        _ => new("PROPOSAL_ISSUE", "Proposal planning was rejected.")
    };

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
