using System.Text.Json;
using AutoGIS.Civil3D.Handoff.Manifest;
using AutoGIS.Civil3D.Handoff.Validation;
using Xunit;

namespace AutoGIS.Civil3D.Handoff.Tests;

public sealed class ContractSchemaTests
{
    [Fact]
    public void Valid_known_datum_manifest_satisfies_schema()
    {
        using JsonDocument json = JsonDocument.Parse(TestManifests.KnownDatum);

        Assert.Empty(ManifestSchemaValidator.Validate(json.RootElement));
    }

    [Fact]
    public void Unknown_root_property_is_rejected()
    {
        using JsonDocument json = JsonDocument.Parse(
            TestManifests.KnownDatum.Replace(
                "\"contract_version\":\"1.0\",",
                "\"contract_version\":\"1.0\",\"unexpected\":true,",
                StringComparison.Ordinal));

        ValidationIssue issue = Assert.Single(ManifestSchemaValidator.Validate(json.RootElement));
        Assert.Equal(IssueCodes.ManifestSchemaViolation, issue.Code);
    }
}
