using System.Globalization;
using System.Text.Json;
using AutoGIS.Civil3D.Handoff.Validation;

namespace AutoGIS.Civil3D.Handoff.Manifest;

internal static class ManifestParser
{
    internal static ManifestParseResult Parse(ReadOnlyMemory<byte> json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            IReadOnlyList<ValidationIssue> schemaIssues = ManifestSchemaValidator.Validate(document.RootElement);
            if (schemaIssues.Count > 0)
            {
                return new ManifestParseResult(null, schemaIssues);
            }

            return ParseValidatedManifest(document.RootElement);
        }
        catch (JsonException)
        {
            return new ManifestParseResult(
                null,
                new[]
                {
                    new ValidationIssue(
                        IssueCodes.ManifestInvalidJson,
                        IssueSeverity.Error,
                        "handoff.json is not valid UTF-8 JSON.")
                });
        }
    }

    private static ManifestParseResult ParseValidatedManifest(JsonElement root)
    {
        List<ValidationIssue> issues = [];

        string contractVersion = GetRequiredString(root, "contract_version");
        string packageIdText = GetRequiredString(root, "package_id");
        string createdUtcText = GetRequiredString(root, "created_utc");
        JsonElement producerElement = root.GetProperty("producer");
        JsonElement surfaceElement = root.GetProperty("surface");
        JsonElement coordinateReferenceElement = root.GetProperty("coordinate_reference");

        Guid packageId = ParsePackageId(packageIdText, issues);
        DateTimeOffset createdUtc = ParseCreatedUtc(createdUtcText, issues);
        ProducerManifest producer = ParseProducer(producerElement, issues);
        SurfaceManifest surface = ParseSurface(surfaceElement, issues);
        CoordinateReferenceManifest coordinateReference = ParseCoordinateReference(coordinateReferenceElement, issues);

        if (issues.Any(issue => issue.Severity == IssueSeverity.Error))
        {
            return new ManifestParseResult(null, issues);
        }

        HandoffManifest manifest = new(
            contractVersion,
            packageId,
            createdUtc,
            producer,
            surface,
            coordinateReference);

        if (coordinateReference.Vertical.Datum.Status == VerticalDatumStatus.Unknown)
        {
            issues.Add(
                new ValidationIssue(
                    IssueCodes.UnknownVerticalDatum,
                    IssueSeverity.Warning,
                    "Vertical datum is unknown and requires review; confirm elevation alignment before use.",
                    "coordinate_reference.vertical.datum"));
        }

        return new ManifestParseResult(manifest, issues);
    }

    private static Guid ParsePackageId(string packageIdText, ICollection<ValidationIssue> issues)
    {
        if (Guid.TryParseExact(packageIdText, "D", out Guid packageId))
        {
            return packageId;
        }

        issues.Add(SemanticIssue("package_id must use UUID format D.", "package_id"));
        return Guid.Empty;
    }

    private static DateTimeOffset ParseCreatedUtc(string createdUtcText, ICollection<ValidationIssue> issues)
    {
        if (createdUtcText.EndsWith('Z') &&
            DateTimeOffset.TryParse(
                createdUtcText,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTimeOffset createdUtc) &&
            createdUtc.Offset == TimeSpan.Zero)
        {
            return createdUtc;
        }

        issues.Add(
            SemanticIssue(
                "created_utc must be normalized to UTC with a trailing Z.",
                "created_utc"));
        return default;
    }

    private static ProducerManifest ParseProducer(JsonElement producerElement, ICollection<ValidationIssue> issues)
    {
        string name = GetRequiredString(producerElement, "name");
        string version = GetRequiredString(producerElement, "version");
        string? sourceCommit = GetOptionalString(producerElement, "source_commit");

        ValidateTrimmedProducerField(name, "producer.name", issues);
        ValidateTrimmedProducerField(version, "producer.version", issues);
        if (IsPathShaped(name) || IsPathShaped(version) ||
            (sourceCommit is not null && IsPathShaped(sourceCommit)))
        {
            issues.Add(
                SemanticIssue(
                    "producer fields must not contain filesystem paths.",
                    "producer"));
        }

        return new ProducerManifest(name, version, sourceCommit);
    }

    private static SurfaceManifest ParseSurface(JsonElement surfaceElement, ICollection<ValidationIssue> issues)
    {
        string name = GetRequiredString(surfaceElement, "name");
        string normalizedName = name.Trim();
        if (normalizedName.Length == 0)
        {
            issues.Add(SemanticIssue("Surface name must not be blank after trimming.", "surface.name"));
        }

        return new SurfaceManifest(
            GetRequiredString(surfaceElement, "filename"),
            GetRequiredString(surfaceElement, "sha256"),
            GetRequiredString(surfaceElement, "landxml_version"),
            normalizedName,
            GetRequiredInt64(surfaceElement, "point_count", "surface.point_count", issues),
            GetRequiredInt64(surfaceElement, "face_count", "surface.face_count", issues));
    }

    private static CoordinateReferenceManifest ParseCoordinateReference(JsonElement coordinateReferenceElement, ICollection<ValidationIssue> issues)
    {
        JsonElement horizontalElement = coordinateReferenceElement.GetProperty("horizontal");
        JsonElement verticalElement = coordinateReferenceElement.GetProperty("vertical");
        JsonElement datumElement = verticalElement.GetProperty("datum");

        VerticalDatumStatus status = datumElement.GetProperty("status").GetString() switch
        {
            "known" => VerticalDatumStatus.Known,
            "unknown" => VerticalDatumStatus.Unknown,
            _ => throw new InvalidOperationException("Schema validation accepted an unsupported vertical datum status.")
        };

        VerticalDatumManifest datum = status == VerticalDatumStatus.Known
            ? new VerticalDatumManifest(
                status,
                GetRequiredString(datumElement, "authority"),
                GetRequiredInt32(
                    datumElement,
                    "code",
                    "coordinate_reference.vertical.datum.code",
                    issues),
                GetRequiredString(datumElement, "name"),
                null)
            : new VerticalDatumManifest(
                status,
                null,
                null,
                null,
                GetOptionalString(datumElement, "note"));

        return new CoordinateReferenceManifest(
            new HorizontalReferenceManifest(
                GetRequiredInt32(
                    horizontalElement,
                    "code",
                    "coordinate_reference.horizontal.code",
                    issues),
                ParseLinearUnit(GetRequiredString(horizontalElement, "unit"))),
            new VerticalReferenceManifest(
                ParseLinearUnit(GetRequiredString(verticalElement, "unit")),
                datum));
    }

    private static void ValidateTrimmedProducerField(
        string value,
        string location,
        ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            issues.Add(SemanticIssue("Producer fields must not be blank or padded with whitespace.", location));
        }
    }

    private static bool IsPathShaped(string value) =>
        value.Contains('/') ||
        value.Contains('\\') ||
        value is "." or ".." ||
        (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':');

    private static LinearUnit ParseLinearUnit(string value) => value switch
    {
        "metre" => LinearUnit.Metre,
        "international_foot" => LinearUnit.InternationalFoot,
        "us_survey_foot" => LinearUnit.UsSurveyFoot,
        _ => throw new InvalidOperationException("Schema validation accepted an unsupported linear unit.")
    };

    private static string GetRequiredString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString()
            ?? throw new InvalidOperationException($"Required string {propertyName} is null.");

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) ? value.GetString() : null;

    private static int GetRequiredInt32(
        JsonElement element,
        string propertyName,
        string location,
        ICollection<ValidationIssue> issues)
    {
        if (element.GetProperty(propertyName).TryGetInt32(out int value))
        {
            return value;
        }

        issues.Add(SemanticIssue($"{propertyName} must fit within a 32-bit signed integer.", location));
        return default;
    }

    private static long GetRequiredInt64(
        JsonElement element,
        string propertyName,
        string location,
        ICollection<ValidationIssue> issues)
    {
        if (element.GetProperty(propertyName).TryGetInt64(out long value))
        {
            return value;
        }

        issues.Add(SemanticIssue($"{propertyName} must fit within a 64-bit signed integer.", location));
        return default;
    }

    private static ValidationIssue SemanticIssue(string message, string location) =>
        new(IssueCodes.ManifestSemanticViolation, IssueSeverity.Error, message, location);
}
