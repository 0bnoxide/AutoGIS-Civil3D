using System.Reflection;
using System.Text.Json;
using AutoGIS.Civil3D.Handoff.Validation;
using Json.Schema;

namespace AutoGIS.Civil3D.Handoff.Manifest;

internal static class ManifestSchemaValidator
{
    private const string ResourceName =
        "AutoGIS.Civil3D.Handoff.Contract.v1.handoff-manifest.schema.json";
    private static readonly Lazy<JsonSchema> Schema = new(BuildSchema);

    internal static IReadOnlyList<ValidationIssue> Validate(JsonElement instance)
    {
        EvaluationResults result = Schema.Value.Evaluate(
            instance,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true
            });

        return result.IsValid
            ? Array.Empty<ValidationIssue>()
            : new[]
            {
                new ValidationIssue(
                    IssueCodes.ManifestSchemaViolation,
                    IssueSeverity.Error,
                    "handoff.json does not satisfy contract version 1.0.",
                    result.InstanceLocation.ToString())
            };
    }

    private static JsonSchema BuildSchema()
    {
        Assembly assembly = typeof(ManifestSchemaValidator).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Missing embedded schema {ResourceName}.");
        using JsonDocument document = JsonDocument.Parse(stream);
        return JsonSchema.Build(
            document.RootElement.Clone(),
            new BuildOptions { Dialect = Dialect.Draft202012 });
    }
}
