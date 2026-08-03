using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;

namespace AutoGIS.Civil3D.FixtureBuilder;

internal sealed record FixtureRecipe(
    string RelativePath,
    string Manifest,
    string Surface,
    CompressionMethod CompressionMethod,
    Action<byte[]>? ArchiveMutation);

internal static class FixtureCatalog
{
    private const string ValidSurface = """
        <?xml version="1.0" encoding="utf-8"?>
        <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
          <Units>
            <Metric linearUnit="meter" elevationUnit="meter" />
          </Units>
          <CoordinateSystem epsgCode="26913" />
          <Surfaces>
            <Surface name="Existing Ground">
              <Definition surfType="TIN">
                <Pnts>
                  <P id="1">0 0 100</P>
                  <P id="2">0 10 101</P>
                  <P id="3">10 0 102</P>
                </Pnts>
                <Faces>
                  <F>1 2 3</F>
                </Faces>
              </Definition>
            </Surface>
          </Surfaces>
        </LandXML>
        """;

    private const string KnownDatumManifest =
        """
        {"contract_version":"1.0","package_id":"9a8ff271-b0d8-46db-809d-a6f72954af20","created_utc":"2026-08-02T00:00:00Z","producer":{"name":"AutoGIS","version":"1.0.0","source_commit":"0123456789abcdef"},"surface":{"filename":"surface.landxml","sha256":"eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9","landxml_version":"1.2","name":"Existing Ground","point_count":4,"face_count":2},"coordinate_reference":{"horizontal":{"kind":"projected","authority":"EPSG","code":2256,"unit":"us_survey_foot"},"vertical":{"unit":"us_survey_foot","direction":"positive_up","datum":{"status":"known","authority":"EPSG","code":5703,"name":"NAVD88 height"}}}}
        """;

    private const string UnknownDatumManifest =
        """
        {"contract_version":"1.0","package_id":"9a8ff271-b0d8-46db-809d-a6f72954af20","created_utc":"2026-08-02T00:00:00Z","producer":{"name":"AutoGIS","version":"1.0.0","source_commit":"0123456789abcdef"},"surface":{"filename":"surface.landxml","sha256":"eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9","landxml_version":"1.2","name":"Existing Ground","point_count":4,"face_count":2},"coordinate_reference":{"horizontal":{"kind":"projected","authority":"EPSG","code":2256,"unit":"us_survey_foot"},"vertical":{"unit":"us_survey_foot","direction":"positive_up","datum":{"status":"unknown","note":"Confirm project datum before import"}}}}
        """;

    private const uint ManifestTooLargeSize = 0x00100001;
    private const uint SurfaceTooLargeSize = 0x80000001;

    internal static void WriteAll(string outputRoot)
    {
        string fullOutputRoot = ValidateOutputRoot(outputRoot);
        Directory.CreateDirectory(fullOutputRoot);
        RejectReparsePoints(fullOutputRoot);

        string stagingRoot = Path.Combine(
            fullOutputRoot,
            $".fixture-build-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            foreach (FixtureRecipe recipe in Recipes())
            {
                string stagedPath = GetBoundedPath(stagingRoot, recipe.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                ZipRecipeWriter.Write(stagedPath, recipe);
            }

            foreach (FixtureRecipe recipe in Recipes())
            {
                string stagedPath = GetBoundedPath(stagingRoot, recipe.RelativePath);
                string destinationPath = GetBoundedPath(fullOutputRoot, recipe.RelativePath);
                string destinationDirectory = Path.GetDirectoryName(destinationPath)!;
                Directory.CreateDirectory(destinationDirectory);
                RejectReparsePoints(destinationDirectory);
                RejectReparseFile(destinationPath);
                File.Move(stagedPath, destinationPath, overwrite: true);
            }
        }
        finally
        {
            DeleteStagingDirectory(fullOutputRoot, stagingRoot);
        }
    }

    private static IReadOnlyList<FixtureRecipe> Recipes()
    {
        string knownManifest = CreateManifest(ValidSurface, knownDatum: true);
        string unknownManifest = CreateManifest(ValidSurface, knownDatum: false);
        string ratioSurface = ValidSurface.Replace(
            "<LandXML ",
            $"<!--{new string('A', 2_000_000)}-->\n<LandXML ",
            StringComparison.Ordinal);

        return
        [
            new("valid/known-vertical-datum.zip", knownManifest, ValidSurface, CompressionMethod.Stored, null),
            new("valid/unknown-vertical-datum.zip", unknownManifest, ValidSurface, CompressionMethod.Stored, null),
            new("invalid/malformed-archive.zip", knownManifest, ValidSurface, CompressionMethod.Stored, null),
            new("invalid/missing-surface.zip", knownManifest, ValidSurface, CompressionMethod.Stored, null),
            new("invalid/extra-entry.zip", knownManifest, ValidSurface, CompressionMethod.Stored, null),
            new("invalid/unsafe-path.zip", knownManifest, ValidSurface, CompressionMethod.Stored, null),
            new("invalid/case-collision.zip", knownManifest, ValidSurface, CompressionMethod.Stored, null),
            new(
                "invalid/symlink-entry.zip",
                knownManifest,
                ValidSurface,
                CompressionMethod.Stored,
                bytes => ZipHeaderMutator.SetUnixMode(bytes, "surface.landxml", 0xA1FF)),
            new(
                "invalid/encrypted-entry.zip",
                knownManifest,
                ValidSurface,
                CompressionMethod.Stored,
                bytes => ZipHeaderMutator.SetEncrypted(bytes, "surface.landxml")),
            new(
                "invalid/unsupported-compression.zip",
                knownManifest,
                ValidSurface,
                CompressionMethod.Stored,
                bytes => ZipHeaderMutator.SetCompressionMethod(bytes, "surface.landxml", 12)),
            new(
                "invalid/manifest-too-large.zip",
                knownManifest,
                ValidSurface,
                CompressionMethod.Stored,
                bytes => ZipHeaderMutator.SetUncompressedSize(bytes, "handoff.json", ManifestTooLargeSize)),
            new(
                "invalid/surface-too-large-declared.zip",
                knownManifest,
                ValidSurface,
                CompressionMethod.Stored,
                bytes => ZipHeaderMutator.SetUncompressedSize(bytes, "surface.landxml", SurfaceTooLargeSize)),
            new(
                "invalid/compression-ratio.zip",
                CreateManifest(ratioSurface, knownDatum: true),
                ratioSurface,
                CompressionMethod.Deflated,
                null),
            new(
                "invalid/manifest-invalid-json.zip",
                "{\"contract_version\":",
                ValidSurface,
                CompressionMethod.Stored,
                null),
            new(
                "invalid/manifest-missing-field.zip",
                knownManifest.Replace(
                    "\"package_id\":\"9a8ff271-b0d8-46db-809d-a6f72954af20\",",
                    string.Empty,
                    StringComparison.Ordinal),
                ValidSurface,
                CompressionMethod.Stored,
                null),
            new(
                "invalid/manifest-unknown-property.zip",
                knownManifest.Insert(1, "\"unexpected\":true,"),
                ValidSurface,
                CompressionMethod.Stored,
                null),
            new(
                "invalid/manifest-version.zip",
                knownManifest.Replace(
                    "\"contract_version\":\"1.0\"",
                    "\"contract_version\":\"2.0\"",
                    StringComparison.Ordinal),
                ValidSurface,
                CompressionMethod.Stored,
                null),
            new(
                "invalid/manifest-timestamp.zip",
                knownManifest.Replace(
                    "\"created_utc\":\"2026-08-02T00:00:00Z\"",
                    "\"created_utc\":\"2026-08-02T00:00:00-06:00\"",
                    StringComparison.Ordinal),
                ValidSurface,
                CompressionMethod.Stored,
                null),
            new(
                "invalid/checksum.zip",
                WithWrongChecksum(knownManifest),
                ValidSurface,
                CompressionMethod.Stored,
                null)
        ];
    }

    private static string CreateManifest(string surface, bool knownDatum)
    {
        string sha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(surface))).ToLowerInvariant();
        string source = knownDatum ? KnownDatumManifest : UnknownDatumManifest;

        return source
            .Replace(
                "eecb977d69ff86eec34d02d881991edd5533eee77e8b854e68cbfcab69ea0af9",
                sha256,
                StringComparison.Ordinal)
            .Replace("\"point_count\":4", "\"point_count\":3", StringComparison.Ordinal)
            .Replace("\"face_count\":2", "\"face_count\":1", StringComparison.Ordinal)
            .Replace("\"code\":2256", "\"code\":26913", StringComparison.Ordinal)
            .Replace("\"unit\":\"us_survey_foot\"", "\"unit\":\"metre\"", StringComparison.Ordinal);
    }

    private static string WithWrongChecksum(string manifest)
    {
        const string marker = "\"sha256\":\"";
        int digestStart = manifest.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        return manifest.Remove(digestStart, 64).Insert(digestStart, new string('0', 64));
    }

    private static string ValidateOutputRoot(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        if (IsWindowsDevicePath(outputRoot))
        {
            throw new ArgumentException(
                "The fixture output path must not use a Windows device namespace.",
                nameof(outputRoot));
        }

        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        string filesystemRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath)!);
        if (PathsEqual(fullPath, filesystemRoot))
        {
            throw new ArgumentException("The fixture output path must not be a filesystem root.", nameof(outputRoot));
        }

        if (File.Exists(fullPath))
        {
            throw new ArgumentException("The fixture output path must be a directory.", nameof(outputRoot));
        }

        RejectReparsePoints(fullPath);
        if (PathsEqual(fullPath, FindRepositoryRoot()))
        {
            throw new ArgumentException("The fixture output path must not be the repository root.", nameof(outputRoot));
        }

        return fullPath;
    }

    private static bool IsWindowsDevicePath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.StartsWith(@"\\?\", StringComparison.Ordinal)
            || normalized.StartsWith(@"\\.\", StringComparison.Ordinal)
            || normalized.StartsWith(@"\??\", StringComparison.Ordinal)
            || normalized.StartsWith(@"\\??\", StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            bool hasSolution = File.Exists(Path.Combine(current.FullName, "AutoGIS.Civil3D.sln"));
            bool hasSpec = File.Exists(Path.Combine(
                current.FullName,
                "docs",
                "superpowers",
                "specs",
                "2026-08-02-landxml-handoff-contract-design.md"));
            if (hasSolution && hasSpec)
            {
                return Path.TrimEndingDirectorySeparator(current.FullName);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the AutoGIS-Civil3D repository root.");
    }

    private static string GetBoundedPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("Fixture paths must be relative.");
        }

        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string prefix = fullRoot + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, PathComparison))
        {
            throw new InvalidDataException("A fixture path escaped the output directory.");
        }

        return fullPath;
    }

    private static void RejectReparsePoints(string path)
    {
        DirectoryInfo? current = new(path);
        while (current is not null)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException("The fixture output path must not contain reparse points.", nameof(path));
            }

            current = current.Parent;
        }
    }

    private static void RejectReparseFile(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException("A fixture destination must not be a reparse point.", nameof(path));
        }
    }

    private static void DeleteStagingDirectory(string outputRoot, string stagingRoot)
    {
        if (!Directory.Exists(stagingRoot))
        {
            return;
        }

        string parent = Directory.GetParent(stagingRoot)?.FullName
            ?? throw new InvalidOperationException("The fixture staging directory has no parent.");
        if (!PathsEqual(parent, outputRoot)
            || !Path.GetFileName(stagingRoot).StartsWith(".fixture-build-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing to delete an unexpected fixture staging directory.");
        }

        if ((File.GetAttributes(stagingRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Refusing to delete a reparse-point staging directory.");
        }

        Directory.Delete(stagingRoot, recursive: true);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            PathComparison);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
