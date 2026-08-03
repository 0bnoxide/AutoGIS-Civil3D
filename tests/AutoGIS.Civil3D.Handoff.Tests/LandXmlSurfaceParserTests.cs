using AutoGIS.Civil3D.Handoff.LandXml;
using AutoGIS.Civil3D.Handoff.Manifest;
using AutoGIS.Civil3D.Handoff.Validation;
using Xunit;

namespace AutoGIS.Civil3D.Handoff.Tests;

public sealed class LandXmlSurfaceParserTests
{
    [Fact]
    public void Parses_one_valid_tin_surface_from_a_forward_only_stream()
    {
        using Stream xml = TestLandXml.Stream(TestLandXml.Valid);

        LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

        Assert.Empty(result.Issues);
        Assert.Equal("Existing Ground", result.Summary!.SurfaceName);
        Assert.Equal("1.2", result.Summary.LandxmlVersion);
        Assert.Equal(3, result.Summary.PointCount);
        Assert.Equal(1, result.Summary.FaceCount);
        Assert.Equal(26913, result.Summary.EpsgCode);
        Assert.Equal(LinearUnit.Metre, result.Summary.HorizontalUnit);
        Assert.Equal(VerticalUnitFamily.Metre, result.Summary.VerticalUnitFamily);
    }

    [Fact]
    public void Malformed_xml_returns_xml001()
    {
        string invalid = TestLandXml.Valid.Replace("</LandXML>", string.Empty, StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlMalformed);
    }

    [Fact]
    public void Dtd_token_split_across_read_chunks_returns_xml002()
    {
        string invalid = TestLandXml.Valid.Replace(
            "<LandXML ",
            "<!DOCTYPE LandXML [<!ENTITY x \"unsafe\">]>\n<LandXML ",
            StringComparison.Ordinal);
        using Stream xml = TestLandXml.Stream(invalid, maximumReadSize: 3);

        LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

        Assert.Null(result.Summary);
        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(IssueCodes.LandXmlForbiddenDtd, issue.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf16_dtd_token_split_across_read_chunks_returns_xml002(bool bigEndian)
    {
        string invalid = TestLandXml.Valid
            .Replace("encoding=\"utf-8\"", "encoding=\"utf-16\"", StringComparison.Ordinal)
            .Replace(
                "<LandXML ",
                "<!DOCTYPE LandXML [<!ENTITY x \"unsafe\">]>\n<LandXML ",
                StringComparison.Ordinal);
        using Stream xml = TestLandXml.Utf16Stream(invalid, bigEndian);

        LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

        Assert.Null(result.Summary);
        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(IssueCodes.LandXmlForbiddenDtd, issue.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Utf32_dtd_token_split_across_read_chunks_returns_xml002(bool bigEndian)
    {
        string invalid = TestLandXml.Valid
            .Replace(
                "encoding=\"utf-8\"",
                bigEndian ? "encoding=\"utf-32BE\"" : "encoding=\"utf-32LE\"",
                StringComparison.Ordinal)
            .Replace(
                "<LandXML ",
                "<!DOCTYPE LandXML [<!ENTITY x \"unsafe\">]>\n<LandXML ",
                StringComparison.Ordinal);
        using Stream xml = TestLandXml.Utf32Stream(invalid, bigEndian);

        LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

        Assert.Null(result.Summary);
        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(IssueCodes.LandXmlForbiddenDtd, issue.Code);
    }

    [Fact]
    public void Forbidden_sequence_stream_detects_dtd_token_split_across_read_chunks()
    {
        using Stream source = TestLandXml.Stream("prefix<!DOCTYPE suffix", maximumReadSize: 3);
        using ForbiddenSequenceReadStream scanning = new(source);

        Assert.Throws<ForbiddenSequenceException>(() => scanning.CopyTo(Stream.Null));
    }

    [Theory]
    [InlineData("http://www.landxml.org/schema/LandXML-1.2", "1.1")]
    [InlineData("http://www.landxml.org/schema/LandXML-1.1", "1.2")]
    public void Wrong_namespace_or_version_returns_xml003(string namespaceUri, string version)
    {
        string invalid = TestLandXml.Valid
            .Replace("http://www.landxml.org/schema/LandXML-1.2", namespaceUri, StringComparison.Ordinal)
            .Replace("version=\"1.2\"", $"version=\"{version}\"", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlUnsupportedVersion);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-an-integer")]
    public void Epsg_code_must_be_a_positive_integer(string epsgCode)
    {
        string invalid = TestLandXml.Valid.Replace(
            "epsgCode=\"26913\"",
            $"epsgCode=\"{epsgCode}\"",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlUnsupportedVersion);
    }

    [Fact]
    public void Two_coordinate_system_declarations_return_xml003()
    {
        const string coordinateSystem = "<CoordinateSystem epsgCode=\"26913\" />";
        string invalid = TestLandXml.Valid.Replace(
            coordinateSystem,
            coordinateSystem + coordinateSystem,
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlUnsupportedVersion);
    }

    [Theory]
    [InlineData("linearUnit=\"meter\"", "linearUnit=\"Meter\"")]
    [InlineData("elevationUnit=\"meter\"", "elevationUnit=\"foot\"")]
    public void Unit_tokens_are_exact(string oldValue, string newValue)
    {
        string invalid = TestLandXml.Valid.Replace(oldValue, newValue, StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlUnsupportedVersion);
    }

    [Fact]
    public void No_surface_returns_xml004()
    {
        string invalid = ReplaceSurfaceContent(string.Empty);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidSurfaceCount);
    }

    [Fact]
    public void Two_surfaces_return_xml004()
    {
        const string secondSurface = """
            <Surface name="Second">
              <Definition surfType="TIN">
                <Pnts />
                <Faces />
              </Definition>
            </Surface>
            """;
        string invalid = TestLandXml.Valid.Replace(
            "</Surfaces>",
            secondSurface + "\n</Surfaces>",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidSurfaceCount);
    }

    [Fact]
    public void Misplaced_same_namespace_surface_returns_xml004()
    {
        string invalid = TestLandXml.Valid.Replace(
            "</LandXML>",
            "<Surface name=\"Misplaced\" />\n</LandXML>",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidSurfaceCount);
    }

    [Fact]
    public void Two_definitions_return_xml005()
    {
        const string secondDefinition = """
            <Definition surfType="TIN">
              <Pnts />
              <Faces />
            </Definition>
            """;
        string invalid = TestLandXml.Valid.Replace(
            "</Surface>",
            secondDefinition + "\n</Surface>",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidDefinitionCount);
    }

    [Fact]
    public void Malformed_point_returns_xml006()
    {
        string invalid = TestLandXml.Valid.Replace(">0 0 100</P>", ">0 100</P>", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidPoint);
    }

    [Fact]
    public void Nested_point_markup_returns_xml006()
    {
        string invalid = TestLandXml.Valid.Replace(
            ">0 0 100</P>",
            "><Value /></P>",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidPoint);
    }

    [Fact]
    public void Nested_same_namespace_surface_inside_a_point_returns_xml004()
    {
        string invalid = TestLandXml.Valid.Replace(
            ">0 0 100</P>",
            "><Surface name=\"Misplaced\" /></P>",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidSurfaceCount);
    }

    [Fact]
    public void Points_and_faces_outside_the_tin_definition_return_xml006()
    {
        string invalid = TestLandXml.Valid
            .Replace("<Definition surfType=\"TIN\">", "<Definition surfType=\"TIN\" />", StringComparison.Ordinal)
            .Replace("</Definition>", string.Empty, StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidPoint);
    }

    [Fact]
    public void Duplicate_point_id_returns_xml007()
    {
        string invalid = TestLandXml.Valid.Replace("<P id=\"2\">", "<P id=\"1\">", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlDuplicatePointId);
    }

    [Fact]
    public void Nonfinite_coordinate_returns_xml008()
    {
        string invalid = TestLandXml.Valid.Replace(">0 10 101</P>", ">NaN 10 101</P>", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlNonfiniteCoordinate);
    }

    [Fact]
    public void Malformed_face_returns_xml009()
    {
        string invalid = TestLandXml.Valid.Replace("<F>1 2 3</F>", "<F>1 2</F>", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidFace);
    }

    [Fact]
    public void Nested_face_markup_returns_xml009()
    {
        string invalid = TestLandXml.Valid.Replace(
            "<F>1 2 3</F>",
            "<F><Vertex>1</Vertex></F>",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidFace);
    }

    [Fact]
    public void Missing_point_reference_returns_xml010()
    {
        string invalid = TestLandXml.Valid.Replace("<F>1 2 3</F>", "<F>1 2 4</F>", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlUnknownPointReference);
    }

    [Fact]
    public void Face_references_must_resolve_before_the_face_is_read()
    {
        const string faces = """
            <Faces>
              <F>1 2 3</F>
            </Faces>
            """;
        string withoutFaces = TestLandXml.Valid.Replace(faces, string.Empty, StringComparison.Ordinal);
        string invalid = withoutFaces.Replace("<Pnts>", faces + "\n<Pnts>", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlUnknownPointReference);
    }

    [Fact]
    public void Repeated_face_vertex_returns_xml011()
    {
        string invalid = TestLandXml.Valid.Replace("<F>1 2 3</F>", "<F>1 2 2</F>", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlRepeatedFaceVertex);
    }

    [Fact]
    public void Near_zero_horizontal_triangle_returns_xml012()
    {
        string invalid = TestLandXml.Valid.Replace(
            ">10 0 102</P>",
            ">0.000000000001 20 102</P>",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlDegenerateFace);
    }

    [Fact]
    public void Triangle_at_the_relative_tolerance_returns_xml012()
    {
        string invalid = TestLandXml.Valid
            .Replace(">0 10 101</P>", ">0 1 101</P>", StringComparison.Ordinal)
            .Replace(">10 0 102</P>", ">0.000000000001 0 102</P>", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlDegenerateFace);
    }

    [Fact]
    public void Triangle_just_above_the_relative_tolerance_is_accepted()
    {
        string valid = TestLandXml.Valid
            .Replace(">0 10 101</P>", ">0 1 101</P>", StringComparison.Ordinal)
            .Replace(">10 0 102</P>", ">0.0000000000011 0 102</P>", StringComparison.Ordinal);
        using Stream xml = TestLandXml.Stream(valid);

        LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

        Assert.Empty(result.Issues);
        Assert.NotNull(result.Summary);
    }

    [Fact]
    public void Finite_extreme_collinear_triangle_returns_xml012()
    {
        string invalid = TestLandXml.Valid
            .Replace(">0 0 100</P>", ">-1e308 -1e308 100</P>", StringComparison.Ordinal)
            .Replace(">0 10 101</P>", ">0 0 101</P>", StringComparison.Ordinal)
            .Replace(">10 0 102</P>", ">1e308 1e308 102</P>", StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlDegenerateFace);
    }

    [Fact]
    public void Oversized_point_scalar_returns_xml006_without_buffering_the_leaf()
    {
        string oversizedScalar = new string('0', 100_000) + "1";
        string invalid = TestLandXml.Valid.Replace(
            ">0 0 100</P>",
            $">{oversizedScalar} 0 100</P>",
            StringComparison.Ordinal);

        AssertPrimaryCode(invalid, IssueCodes.LandXmlInvalidPoint);
    }

    [Theory]
    [InlineData("meter", "Metre")]
    [InlineData("foot", "InternationalFoot")]
    [InlineData("USSurveyFoot", "UsSurveyFoot")]
    public void Maps_horizontal_units_exactly(string landXmlUnit, string expected)
    {
        string xmlText = TestLandXml.Valid.Replace(
            "linearUnit=\"meter\"",
            $"linearUnit=\"{landXmlUnit}\"",
            StringComparison.Ordinal);
        using Stream xml = TestLandXml.Stream(xmlText);

        LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

        Assert.Empty(result.Issues);
        Assert.Equal(expected, result.Summary!.HorizontalUnit.ToString());
    }

    [Theory]
    [InlineData("meter", "Metre")]
    [InlineData("feet", "Foot")]
    public void Maps_vertical_unit_families(string elevationUnit, string expected)
    {
        string xmlText = TestLandXml.Valid.Replace(
            "elevationUnit=\"meter\"",
            $"elevationUnit=\"{elevationUnit}\"",
            StringComparison.Ordinal);
        using Stream xml = TestLandXml.Stream(xmlText);

        LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

        Assert.Empty(result.Issues);
        Assert.Equal(expected, result.Summary!.VerticalUnitFamily.ToString());
    }

    private static string ReplaceSurfaceContent(string replacement)
    {
        const string start = "<Surface name=\"Existing Ground\">";
        const string end = "</Surface>";
        int startIndex = TestLandXml.Valid.IndexOf(start, StringComparison.Ordinal);
        int endIndex = TestLandXml.Valid.IndexOf(end, startIndex, StringComparison.Ordinal) + end.Length;
        return TestLandXml.Valid.Remove(startIndex, endIndex - startIndex).Insert(startIndex, replacement);
    }

    private static void AssertPrimaryCode(string xmlText, string expectedCode)
    {
        using Stream xml = TestLandXml.Stream(xmlText);

        LandXmlParseResult result = LandXmlSurfaceParser.Parse(xml);

        Assert.Null(result.Summary);
        ValidationIssue issue = Assert.Single(result.Issues);
        Assert.Equal(expectedCode, issue.Code);
        Assert.Equal(IssueSeverity.Error, issue.Severity);
    }
}
