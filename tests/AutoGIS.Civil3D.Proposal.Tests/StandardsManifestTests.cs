using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace AutoGIS.Civil3D.Proposal.Tests;

public sealed class StandardsManifestTests
{
    internal static byte[] FixtureBytes() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "synthetic-standards.json"));

    internal static JsonNode Fixture() => JsonNode.Parse(FixtureBytes())!;
    internal static ManifestResult Parse(JsonNode node) => StandardsManifest.Parse(Encoding.UTF8.GetBytes(node.ToJsonString()));

    internal static JsonNode FixtureWithMappings(params string[] inputs)
    {
        var json = Fixture();
        json["propertyMappings"] = new JsonArray(json["propertyMappings"]!.AsArray()
            .Where(p => inputs.Contains(p!["input"]!.GetValue<string>(), StringComparer.Ordinal))
            .Select(p => p!.DeepClone()).ToArray());
        return json;
    }

    [Theory]
    [InlineData("ClientNumber")]
    [InlineData("ClientName", "SiteName", "ClientNumber", "ProjectNumber", "ProposalNumber", "SiteAddress", "ProjectManager")]
    public void Declared_known_mapping_subset_is_accepted(params string[] inputs)
    {
        var result = Parse(FixtureWithMappings(inputs));
        Assert.Empty(result.Issues);
        Assert.NotNull(result.Manifest);
        Assert.Equal(inputs.Order(StringComparer.Ordinal), result.Manifest.PropertyMappings.Select(p => p.Input));
        Assert.Equal(result.Manifest.ToJson(), StandardsManifest.Parse(Encoding.UTF8.GetBytes(result.Manifest.ToJson())).Manifest!.ToJson());
    }

    [Theory]
    [InlineData("missingClientNumber")]
    [InlineData("unknownInput")]
    [InlineData("duplicateInput")]
    [InlineData("caseVariantInput")]
    [InlineData("dstCollision")]
    [InlineData("titleBlockCollision")]
    public void Incomplete_or_ambiguous_mapping_subset_has_no_value(string mutation)
    {
        var json = FixtureWithMappings("ClientName", "ClientNumber");
        var mappings = json["propertyMappings"]!.AsArray();
        switch (mutation)
        {
            case "missingClientNumber": mappings.RemoveAt(1); break;
            case "unknownInput": mappings[0]!["input"] = "UnknownInput"; break;
            case "duplicateInput": mappings[0]!["input"] = "ClientNumber"; break;
            case "caseVariantInput": mappings[0]!["input"] = "clientnumber"; break;
            case "dstCollision": mappings[0]!["dstProperty"] = "test-clientnumber"; break;
            case "titleBlockCollision": mappings[0]!["titleBlockAttribute"] = "test_clientnumber"; break;
        }
        AssertInvalid(Parse(json), ProposalIssueCodes.InvalidManifest);
    }

    [Fact]
    public void Canonical_manifest_roundtrips_all_operational_data()
    {
        var first = StandardsManifest.Parse(FixtureBytes()).Manifest!;
        var second = StandardsManifest.Parse(Encoding.UTF8.GetBytes(first.ToJson()));
        Assert.Empty(second.Issues);
        Assert.Equal(first.ToJson(), second.Manifest!.ToJson());
    }

    [Theory]
    [InlineData("C:relative", false)]
    [InlineData("/root", false)]
    [InlineData("//server/share", true)]
    [InlineData("//server/share/../escaped", false)]
    [InlineData("//?/C:/device", false)]
    [InlineData("C:/", true)]
    [InlineData("C:/root/CON", false)]
    public void Base_roots_follow_explicit_windows_drive_and_unc_rules(string root, bool valid)
    {
        var json = Fixture();
        json["baseRoots"]![0]!["path"] = root;
        var result = Parse(json);
        Assert.Equal(valid, result.Manifest is not null);
        if (!valid) AssertInvalid(result, ProposalIssueCodes.UnsafePath);
    }

    [Fact]
    public void Complete_synthetic_manifest_is_accepted()
    {
        var result = StandardsManifest.Parse(FixtureBytes());
        Assert.Empty(result.Issues);
        Assert.NotNull(result.Manifest);
    }

    [Theory]
    [InlineData("{", ProposalIssueCodes.InvalidJson)]
    [InlineData("{\"version\":999}", ProposalIssueCodes.UnsupportedVersion)]
    [InlineData("{\"version\":1,\"version\":2}", ProposalIssueCodes.DuplicateProperty)]
    [InlineData("{\"version\":1,\"a\":[{\"x\":0,\"x\":1}]}", ProposalIssueCodes.DuplicateProperty)]
    [InlineData("{\"version\":1,\"a\":{\"x\":0,\"\\u0078\":1}}", ProposalIssueCodes.DuplicateProperty)]
    [InlineData("{\"version\":1,}", ProposalIssueCodes.InvalidJson)]
    [InlineData("{\"version\":/*comment*/1}", ProposalIssueCodes.InvalidJson)]
    [InlineData("[]", ProposalIssueCodes.InvalidManifest)]
    public void Invalid_json_returns_a_stable_code(string json, string code) =>
        AssertInvalid(StandardsManifest.Parse(Encoding.UTF8.GetBytes(json)), code);

    [Fact]
    public void Malformed_utf8_is_not_replaced()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"version\":1,\"label\":\"X\"}");
        bytes[Array.IndexOf(bytes, (byte)'X')] = 0xff;
        AssertInvalid(StandardsManifest.Parse(bytes), ProposalIssueCodes.InvalidJson);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("nestedUnknown")]
    [InlineData("duplicateRole")]
    [InlineData("missingSupport")]
    [InlineData("duplicateYear")]
    [InlineData("duplicateProfile")]
    [InlineData("duplicateSheetNumber")]
    [InlineData("invalidGeometry")]
    [InlineData("missingPlaceholder")]
    public void Incomplete_or_conflicting_standards_have_no_value(string mutation)
    {
        var json = Fixture();
        switch (mutation)
        {
            case "unknown": json["invented"] = true; break;
            case "missing": json.AsObject().Remove("modelTemplate"); break;
            case "null": json["models"] = null; break;
            case "nestedUnknown": json["profiles"]![0]!["plot"]!["unknown"] = 1; break;
            case "duplicateRole": json["models"]![1]!["role"] = "BaseModel"; break;
            case "missingSupport": json["supportRecords"]!.AsArray().RemoveAt(0); break;
            case "duplicateYear": json["baseRoots"]!.AsArray().Add(json["baseRoots"]![0]!.DeepClone()); break;
            case "duplicateProfile": json["profiles"]!.AsArray().Add(json["profiles"]![0]!.DeepClone()); break;
            case "duplicateSheetNumber": json["sheets"]![1]!["number"] = "test-01"; break;
            case "invalidGeometry": json["profiles"]![0]!["placeholders"]![0]!["rectangle"]!["width"] = -1; break;
            case "missingPlaceholder": json["profiles"]![0]!["placeholders"]!.AsArray().RemoveAt(0); break;
        }
        AssertInvalid(Parse(json), ProposalIssueCodes.InvalidManifest);
    }

    [Theory]
    [InlineData("../escaped.dwg")]
    [InlineData("/rooted.dwg")]
    [InlineData("C:relative.dwg")]
    [InlineData("C:/rooted.dwg")]
    [InlineData("\\\\server\\share\\evil.dwg")]
    [InlineData("Model/file.dwg:stream")]
    [InlineData("Model/NUL.dwg")]
    [InlineData("Model/COM1.dwg")]
    [InlineData("Model/LPT¹.dwg")]
    [InlineData("Model/CONIN$.dwg")]
    [InlineData("Model/file.dwg.")]
    [InlineData("Model/file.dwg ")]
    [InlineData("Model//file.dwg")]
    [InlineData("Model/./file.dwg")]
    [InlineData("Model/bad\u0001.dwg")]
    [InlineData("Model/bad?.dwg")]
    public void Unsafe_artifact_paths_are_rejected_on_every_host(string path)
    {
        var json = Fixture();
        json["models"]![0]!["path"] = path;
        AssertInvalid(Parse(json), ProposalIssueCodes.UnsafePath);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("folder")]
    [InlineData("fileParent")]
    [InlineData("folderCase")]
    public void Case_insensitive_files_and_folders_cannot_collide(string mutation)
    {
        var json = Fixture();
        switch (mutation)
        {
            case "file": json["models"]![1]!["path"] = "model/base.DWG"; break;
            case "folder": json["folders"]!.AsArray().Add("Model/Base.dwg"); break;
            case "fileParent": json["folders"]!.AsArray().Add("Model/Base.dwg/child"); break;
            case "folderCase": json["folders"]!.AsArray().Add("model"); break;
        }
        AssertInvalid(Parse(json), ProposalIssueCodes.OutputCollision);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknownRole")]
    [InlineData("cycle")]
    [InlineData("duplicate")]
    [InlineData("attach")]
    public void Reference_graph_must_be_complete_unambiguous_and_overlay(string mutation)
    {
        var json = Fixture();
        var edges = json["xrefs"]!.AsArray();
        switch (mutation)
        {
            case "missing": edges.RemoveAt(0); break;
            case "unknownRole": edges[0]!["referenceRole"] = "MissingModel"; break;
            case "cycle":
                var edge = edges[0]!.DeepClone(); edge["hostRole"] = "BaseModel";
                edge["referenceRole"] = "ProposedDesignModel"; edges.Add(edge); break;
            case "duplicate": edges.Add(edges[0]!.DeepClone()); break;
            case "attach": edges[0]!["mode"] = "Attach"; break;
        }
        AssertInvalid(Parse(json), ProposalIssueCodes.InvalidReferences);
    }

    private static void AssertInvalid(ManifestResult result, string code)
    {
        Assert.Null(result.Manifest);
        Assert.Contains(result.Issues, issue => issue.Code == code);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"version\":1,\"version\":2}")]
    [InlineData("{\"version\":999}")]
    public void Invalid_or_ambiguous_manifest_has_no_value(string json)
    {
        var result = StandardsManifest.Parse(Encoding.UTF8.GetBytes(json));
        Assert.Null(result.Manifest);
        Assert.NotEmpty(result.Issues);
    }
}
