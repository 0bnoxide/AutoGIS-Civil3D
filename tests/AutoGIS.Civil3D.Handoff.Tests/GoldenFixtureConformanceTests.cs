using System.Security.Cryptography;
using AutoGIS.Civil3D.FixtureBuilder;
using AutoGIS.Civil3D.Handoff.Validation;
using Xunit;

namespace AutoGIS.Civil3D.Handoff.Tests;

public sealed class GoldenFixtureConformanceTests
{
    [Theory]
    [InlineData("valid/known-vertical-datum.zip", ValidationStatus.Valid, null)]
    [InlineData("valid/unknown-vertical-datum.zip", ValidationStatus.ValidWithWarnings, "WRN001")]
    [InlineData("invalid/malformed-archive.zip", ValidationStatus.Invalid, "ZIP001")]
    [InlineData("invalid/missing-surface.zip", ValidationStatus.Invalid, "ZIP003")]
    [InlineData("invalid/extra-entry.zip", ValidationStatus.Invalid, "ZIP004")]
    [InlineData("invalid/unsafe-path.zip", ValidationStatus.Invalid, "ZIP005")]
    [InlineData("invalid/case-collision.zip", ValidationStatus.Invalid, "ZIP006")]
    [InlineData("invalid/symlink-entry.zip", ValidationStatus.Invalid, "ZIP007")]
    [InlineData("invalid/encrypted-entry.zip", ValidationStatus.Invalid, "ZIP008")]
    [InlineData("invalid/unsupported-compression.zip", ValidationStatus.Invalid, "ZIP009")]
    [InlineData("invalid/manifest-too-large.zip", ValidationStatus.Invalid, "ZIP010")]
    [InlineData("invalid/surface-too-large-declared.zip", ValidationStatus.Invalid, "ZIP011")]
    [InlineData("invalid/compression-ratio.zip", ValidationStatus.Invalid, "ZIP012")]
    [InlineData("invalid/manifest-invalid-json.zip", ValidationStatus.Invalid, "MAN001")]
    [InlineData("invalid/manifest-missing-field.zip", ValidationStatus.Invalid, "MAN002")]
    [InlineData("invalid/manifest-unknown-property.zip", ValidationStatus.Invalid, "MAN002")]
    [InlineData("invalid/manifest-version.zip", ValidationStatus.Invalid, "MAN002")]
    [InlineData("invalid/manifest-timestamp.zip", ValidationStatus.Invalid, "MAN003")]
    [InlineData("invalid/checksum.zip", ValidationStatus.Invalid, "INT001")]
    [InlineData("invalid/xml-malformed.zip", ValidationStatus.Invalid, "XML001")]
    [InlineData("invalid/xml-dtd.zip", ValidationStatus.Invalid, "XML002")]
    [InlineData("invalid/xml-version.zip", ValidationStatus.Invalid, "XML003")]
    [InlineData("invalid/xml-no-surface.zip", ValidationStatus.Invalid, "XML004")]
    [InlineData("invalid/xml-multiple-surfaces.zip", ValidationStatus.Invalid, "XML004")]
    [InlineData("invalid/xml-multiple-definitions.zip", ValidationStatus.Invalid, "XML005")]
    [InlineData("invalid/xml-invalid-point.zip", ValidationStatus.Invalid, "XML006")]
    [InlineData("invalid/xml-duplicate-point-id.zip", ValidationStatus.Invalid, "XML007")]
    [InlineData("invalid/xml-nonfinite-coordinate.zip", ValidationStatus.Invalid, "XML008")]
    [InlineData("invalid/xml-invalid-face.zip", ValidationStatus.Invalid, "XML009")]
    [InlineData("invalid/xml-unknown-point-reference.zip", ValidationStatus.Invalid, "XML010")]
    [InlineData("invalid/xml-repeated-face-vertex.zip", ValidationStatus.Invalid, "XML011")]
    [InlineData("invalid/xml-degenerate-face.zip", ValidationStatus.Invalid, "XML012")]
    [InlineData("invalid/surface-name-mismatch.zip", ValidationStatus.Invalid, "XCK001")]
    [InlineData("invalid/point-count-mismatch.zip", ValidationStatus.Invalid, "XCK002")]
    [InlineData("invalid/face-count-mismatch.zip", ValidationStatus.Invalid, "XCK003")]
    [InlineData("invalid/epsg-mismatch.zip", ValidationStatus.Invalid, "XCK004")]
    [InlineData("invalid/horizontal-unit-mismatch.zip", ValidationStatus.Invalid, "XCK005")]
    [InlineData("invalid/vertical-unit-family-mismatch.zip", ValidationStatus.Invalid, "XCK006")]
    [InlineData("invalid/vertical-direction-invalid.zip", ValidationStatus.Invalid, "MAN002")]
    [InlineData("invalid/vertical-datum-invalid.zip", ValidationStatus.Invalid, "MAN002")]
    public void Checked_in_package_has_expected_status_and_primary_code(
        string relativePath,
        ValidationStatus expectedStatus,
        string? expectedPrimaryCode)
    {
        string path = Path.Combine(TestRepository.Root, "fixtures", "v1", relativePath);

        ValidationReport report = new BundleValidator().ValidateBundle(path);

        Assert.Equal(expectedStatus, report.Status);
        Assert.Equal(expectedPrimaryCode, report.Issues.FirstOrDefault()?.Code);
    }

    [Fact]
    public void Regenerated_packages_match_every_checked_in_zip_byte_for_byte()
    {
        string generatedRoot = Path.Combine(
            Path.GetTempPath(),
            "AutoGIS.Civil3D.Handoff.Tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            FixtureCatalog.WriteAll(generatedRoot);
            string committedRoot = Path.Combine(TestRepository.Root, "fixtures", "v1");
            string[] committedPaths = RelativeZipPaths(committedRoot);
            string[] generatedPaths = RelativeZipPaths(generatedRoot);

            Assert.Equal(committedPaths, generatedPaths);
            foreach (string relativePath in committedPaths)
            {
                byte[] committedHash = SHA256.HashData(File.ReadAllBytes(
                    Path.Combine(committedRoot, relativePath)));
                byte[] generatedHash = SHA256.HashData(File.ReadAllBytes(
                    Path.Combine(generatedRoot, relativePath)));
                Assert.Equal(committedHash, generatedHash);
            }
        }
        finally
        {
            if (Directory.Exists(generatedRoot))
            {
                Directory.Delete(generatedRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Fixture_catalog_rejects_empty_output_path()
    {
        Assert.Throws<ArgumentException>(() => FixtureCatalog.WriteAll(" "));
    }

    [Fact]
    public void Fixture_catalog_rejects_filesystem_root()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.Throws<ArgumentException>(() => FixtureCatalog.WriteAll(root));
    }

    [Fact]
    public void Fixture_catalog_rejects_repository_root()
    {
        Assert.Throws<ArgumentException>(() => FixtureCatalog.WriteAll(TestRepository.Root));
    }

    [Fact]
    public void Fixture_catalog_rejects_windows_device_namespace_path()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string ordinaryPath = Path.Combine(
            Path.GetTempPath(),
            "AutoGIS.Civil3D.Handoff.Tests",
            Guid.NewGuid().ToString("N"));
        string devicePath = $@"\\?\{ordinaryPath}";
        try
        {
            Assert.Throws<ArgumentException>(() => FixtureCatalog.WriteAll(devicePath));
        }
        finally
        {
            if (Directory.Exists(ordinaryPath))
            {
                Directory.Delete(ordinaryPath, recursive: true);
            }
        }
    }

    [Fact]
    public void Fixture_catalog_rejects_reparse_point_output_path()
    {
        string testRoot = Path.Combine(
            Path.GetTempPath(),
            "AutoGIS.Civil3D.Handoff.Tests",
            Guid.NewGuid().ToString("N"));
        string targetPath = Path.Combine(testRoot, "target");
        string linkPath = Path.Combine(testRoot, "link");
        try
        {
            Directory.CreateDirectory(targetPath);
            Directory.CreateSymbolicLink(linkPath, targetPath);

            Assert.Throws<ArgumentException>(() => FixtureCatalog.WriteAll(linkPath));
            Assert.Empty(Directory.GetFiles(targetPath, "*.zip", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(linkPath))
            {
                Directory.Delete(linkPath);
            }

            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
            }
        }
    }

    private static string[] RelativeZipPaths(string root) =>
        Directory.GetFiles(root, "*.zip", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
