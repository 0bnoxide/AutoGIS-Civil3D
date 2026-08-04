using System.Text;
using System.Text.Json;
using AutoGIS.Civil3D.Handoff.Manifest;
using AutoGIS.Civil3D.Handoff.Validation;
using Xunit;

namespace AutoGIS.Civil3D.Handoff.Tests;

public sealed class ManifestParserTests
{
    [Fact]
    public void Known_datum_returns_typed_manifest_without_issues()
    {
        ManifestParseResult result = ManifestParser.Parse(
            Encoding.UTF8.GetBytes(TestManifests.KnownDatum));

        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Issues);
        Assert.Equal(2256, result.Manifest.CoordinateReference.Horizontal.Code);
        Assert.Equal(VerticalDatumStatus.Known, result.Manifest.CoordinateReference.Vertical.Datum.Status);
    }

    [Fact]
    public void Unknown_datum_returns_review_warning()
    {
        ManifestParseResult result = ManifestParser.Parse(
            Encoding.UTF8.GetBytes(TestManifests.UnknownDatum));

        ValidationIssue warning = Assert.Single(result.Issues);
        Assert.Equal(IssueCodes.UnknownVerticalDatum, warning.Code);
        Assert.Equal(IssueSeverity.Warning, warning.Severity);
        Assert.Contains(
            "confirm elevation alignment before use",
            warning.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Offset_timestamp_passes_schema_but_fails_normalized_utc_semantics()
    {
        byte[] json = Encoding.UTF8.GetBytes(
            TestManifests.WithCreatedUtc("2026-08-02T00:00:00-06:00"));

        ManifestParseResult result = ManifestParser.Parse(json);

        Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSemanticViolation);
    }

    [Fact]
    public void Date_without_time_fails_schema()
    {
        byte[] json = Encoding.UTF8.GetBytes(
            TestManifests.WithCreatedUtc("2026-08-02"));

        ManifestParseResult result = ManifestParser.Parse(json);

        Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSchemaViolation);
    }

    [Fact]
    public void Invalid_utf8_returns_invalid_json_issue()
    {
        ManifestParseResult result = ManifestParser.Parse(new byte[] { 0xc3, 0x28 });

        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Null(result.Manifest);
        Assert.Equal(IssueCodes.ManifestInvalidJson, issue.Code);
    }

    [Fact]
    public void Malformed_json_returns_invalid_json_issue()
    {
        ManifestParseResult result = ManifestParser.Parse(Encoding.UTF8.GetBytes("{\"contract_version\":"));

        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Null(result.Manifest);
        Assert.Equal(IssueCodes.ManifestInvalidJson, issue.Code);
    }

    [Theory]
    [InlineData("\"name\":\"AutoGIS\"")]
    [InlineData("\"name\":\"Existing Ground\"")]
    public void Blank_trimmed_names_fail_semantics(string oldValue)
    {
        string json = TestManifests.KnownDatum.Replace(
            oldValue,
            "\"name\":\"   \"",
            StringComparison.Ordinal);

        ManifestParseResult result = ManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSemanticViolation);
    }

    [Fact]
    public void Path_shaped_producer_name_fails_semantics()
    {
        string json = TestManifests.KnownDatum.Replace(
            "\"name\":\"AutoGIS\"",
            "\"name\":\"C:\\\\Users\\\\name\"",
            StringComparison.Ordinal);

        ManifestParseResult result = ManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSemanticViolation);
    }

    [Theory]
    [InlineData("..\\Users\\alice")]
    [InlineData("folder/file")]
    [InlineData("C:relative")]
    [InlineData(".")]
    [InlineData("..")]
    public void Relative_path_shaped_producer_name_fails_semantics(string producerName)
    {
        string json = TestManifests.KnownDatum.Replace(
            "\"AutoGIS\"",
            JsonSerializer.Serialize(producerName),
            StringComparison.Ordinal);

        ManifestParseResult result = ManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSemanticViolation);
    }

    [Theory]
    [InlineData("\"code\":2256", "\"code\":2147483648")]
    [InlineData("\"code\":5703", "\"code\":2147483648")]
    [InlineData("\"point_count\":4", "\"point_count\":9223372036854775808")]
    [InlineData("\"face_count\":2", "\"face_count\":9223372036854775808")]
    public void Schema_valid_oversized_integer_returns_semantic_issue(string oldValue, string newValue)
    {
        string json = TestManifests.KnownDatum.Replace(oldValue, newValue, StringComparison.Ordinal);

        ManifestParseResult result = ManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSemanticViolation);
    }

    [Fact]
    public void C1_control_character_in_producer_name_fails_semantics()
    {
        string json = TestManifests.KnownDatum.Replace(
            "\"name\":\"AutoGIS\"",
            "\"name\":\"Auto\\u0085GIS\"",
            StringComparison.Ordinal);

        ManifestParseResult result = ManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSemanticViolation);
    }

    [Fact]
    public void Whitespace_padded_producer_version_fails_semantics()
    {
        string json = TestManifests.KnownDatum.Replace(
            "\"version\":\"1.0.0\"",
            "\"version\":\" 1.0.0 \"",
            StringComparison.Ordinal);

        ManifestParseResult result = ManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Issues, issue => issue.Code == IssueCodes.ManifestSemanticViolation);
    }

    [Fact]
    public void Whitespace_padded_surface_name_is_normalized_without_an_issue()
    {
        string json = TestManifests.KnownDatum.Replace(
            "\"name\":\"Existing Ground\"",
            "\"name\":\" Existing Ground \"",
            StringComparison.Ordinal);

        ManifestParseResult result = ManifestParser.Parse(Encoding.UTF8.GetBytes(json));

        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Issues);
        Assert.Equal("Existing Ground", result.Manifest.Surface.Name);
    }
}
