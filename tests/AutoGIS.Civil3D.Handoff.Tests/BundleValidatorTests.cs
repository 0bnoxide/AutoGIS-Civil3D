using AutoGIS.Civil3D.Handoff.Manifest;
using AutoGIS.Civil3D.Handoff.Validation;
using Xunit;

namespace AutoGIS.Civil3D.Handoff.Tests;

public sealed class BundleValidatorTests
{
    [Fact]
    public void Known_datum_bundle_is_valid_with_verified_metadata()
    {
        string path = TestPackageBuilder.CreateValid(VerticalDatumStatus.Known);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Valid, report.Status);
            Assert.Empty(report.Issues);
            VerifiedPackageMetadata metadata = Assert.IsType<VerifiedPackageMetadata>(report.Metadata);
            Assert.Equal(Guid.Parse("9a8ff271-b0d8-46db-809d-a6f72954af20"), metadata.PackageId);
            Assert.Equal("Existing Ground", metadata.SurfaceName);
            Assert.Equal(3, metadata.PointCount);
            Assert.Equal(1, metadata.FaceCount);
            Assert.Equal(26913, metadata.EpsgCode);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Unknown_datum_is_valid_with_warning_and_verified_metadata()
    {
        string path = TestPackageBuilder.CreateValid(VerticalDatumStatus.Unknown);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.ValidWithWarnings, report.Status);
            Assert.Equal(IssueCodes.UnknownVerticalDatum, Assert.Single(report.Issues).Code);
            Assert.NotNull(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Theory]
    [InlineData("international_foot")]
    [InlineData("us_survey_foot")]
    public void Landxml_feet_accepts_either_manifest_foot_definition(string manifestUnit)
    {
        string landXml = TestLandXml.Valid.Replace(
            "elevationUnit=\"meter\"",
            "elevationUnit=\"feet\"",
            StringComparison.Ordinal);
        string manifest = TestPackageBuilder.CreateManifest(landXml).Replace(
            "\"vertical\":{\"unit\":\"metre\"",
            $"\"vertical\":{{\"unit\":\"{manifestUnit}\"",
            StringComparison.Ordinal);
        string path = TestPackageBuilder.CreateBundle(manifest, landXml);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Valid, report.Status);
            Assert.Empty(report.Issues);
            Assert.NotNull(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Checksum_failure_stops_before_malformed_xml_is_parsed()
    {
        const string malformedLandXml = "<not-landxml>";
        string manifest = WithWrongChecksum(TestPackageBuilder.CreateManifest(malformedLandXml));
        string path = TestPackageBuilder.CreateBundle(manifest, malformedLandXml);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Invalid, report.Status);
            Assert.Equal(IssueCodes.ChecksumMismatch, Assert.Single(report.Issues).Code);
            Assert.Null(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Manifest_warning_is_retained_before_checksum_error()
    {
        string manifest = WithWrongChecksum(
            TestPackageBuilder.CreateManifest(
                TestLandXml.Valid,
                VerticalDatumStatus.Unknown));
        string path = TestPackageBuilder.CreateBundle(manifest, TestLandXml.Valid);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Invalid, report.Status);
            Assert.Equal(
                [IssueCodes.UnknownVerticalDatum, IssueCodes.ChecksumMismatch],
                report.Issues.Select(issue => issue.Code));
            Assert.Null(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Theory]
    [InlineData("\"name\":\"Existing Ground\"", "\"name\":\"Design Ground\"", "XCK001")]
    [InlineData("\"point_count\":3", "\"point_count\":4", "XCK002")]
    [InlineData("\"face_count\":1", "\"face_count\":2", "XCK003")]
    [InlineData("\"code\":26913", "\"code\":26914", "XCK004")]
    [InlineData(
        "\"horizontal\":{\"kind\":\"projected\",\"authority\":\"EPSG\",\"code\":26913,\"unit\":\"metre\"}",
        "\"horizontal\":{\"kind\":\"projected\",\"authority\":\"EPSG\",\"code\":26913,\"unit\":\"international_foot\"}",
        "XCK005")]
    [InlineData(
        "\"vertical\":{\"unit\":\"metre\"",
        "\"vertical\":{\"unit\":\"us_survey_foot\"",
        "XCK006")]
    public void Manifest_to_landxml_mismatch_returns_stable_cross_check_code(
        string oldValue,
        string newValue,
        string expectedCode)
    {
        string manifest = TestPackageBuilder.CreateManifest(TestLandXml.Valid).Replace(
            oldValue,
            newValue,
            StringComparison.Ordinal);
        string path = TestPackageBuilder.CreateBundle(manifest, TestLandXml.Valid);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Invalid, report.Status);
            Assert.Equal(expectedCode, Assert.Single(report.Issues).Code);
            Assert.Null(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Container_error_stops_before_manifest_processing()
    {
        string path = TestPackageBuilder.Create(PackageFault.Malformed);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Invalid, report.Status);
            Assert.Equal(IssueCodes.InvalidArchive, Assert.Single(report.Issues).Code);
            Assert.Null(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Manifest_error_stops_before_checksum_and_xml_processing()
    {
        string path = TestPackageBuilder.CreateBundle("not-json", "<not-landxml>");
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Invalid, report.Status);
            Assert.Equal(IssueCodes.ManifestInvalidJson, Assert.Single(report.Issues).Code);
            Assert.Null(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Xml_error_stops_before_cross_checks()
    {
        const string malformedLandXml = "<not-landxml>";
        string path = TestPackageBuilder.CreateBundle(
            TestPackageBuilder.CreateManifest(malformedLandXml),
            malformedLandXml);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Invalid, report.Status);
            Assert.Equal(IssueCodes.LandXmlMalformed, Assert.Single(report.Issues).Code);
            Assert.Null(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Manifest_warning_is_retained_before_ordered_cross_check_errors()
    {
        string manifest = TestPackageBuilder.CreateManifest(
                TestLandXml.Valid,
                VerticalDatumStatus.Unknown)
            .Replace("\"name\":\"Existing Ground\"", "\"name\":\"Design Ground\"", StringComparison.Ordinal)
            .Replace("\"point_count\":3", "\"point_count\":4", StringComparison.Ordinal);
        string path = TestPackageBuilder.CreateBundle(manifest, TestLandXml.Valid);
        try
        {
            ValidationReport report = new BundleValidator().ValidateBundle(path);

            Assert.Equal(ValidationStatus.Invalid, report.Status);
            Assert.Equal(
                [IssueCodes.UnknownVerticalDatum, IssueCodes.SurfaceNameMismatch, IssueCodes.PointCountMismatch],
                report.Issues.Select(issue => issue.Code));
            Assert.Null(report.Metadata);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    private static string WithWrongChecksum(string manifest)
    {
        const string marker = "\"sha256\":\"";
        int digestStart = manifest.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return manifest.Remove(digestStart, 64).Insert(digestStart, new string('0', 64));
    }
}
