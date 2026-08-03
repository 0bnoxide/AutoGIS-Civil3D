using System.Text;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip;

namespace AutoGIS.Civil3D.FixtureBuilder;

internal static class ZipRecipeWriter
{
    private const int UnixHostSystem = 3;
    private const int RegularFileAttributes = unchecked((int)0x81A40000);
    private static readonly DateTime EntryTimestamp = new(
        2026,
        8,
        2,
        0,
        0,
        0,
        DateTimeKind.Unspecified);

    internal static void Write(string path, FixtureRecipe recipe)
    {
        if (recipe.RelativePath == "invalid/malformed-archive.zip")
        {
            File.WriteAllBytes(path, "not a ZIP archive"u8.ToArray());
            return;
        }

        List<EntryContent> entries =
        [
            new("handoff.json", Encoding.UTF8.GetBytes(recipe.Manifest))
        ];

        if (recipe.RelativePath != "invalid/missing-surface.zip")
        {
            string surfaceName = recipe.RelativePath == "invalid/unsafe-path.zip"
                ? "../surface.landxml"
                : "surface.landxml";
            entries.Add(new EntryContent(surfaceName, Encoding.UTF8.GetBytes(recipe.Surface)));
        }

        if (recipe.RelativePath == "invalid/extra-entry.zip")
        {
            entries.Add(new EntryContent("unexpected.txt", "unexpected"u8.ToArray()));
        }
        else if (recipe.RelativePath == "invalid/case-collision.zip")
        {
            entries.Add(new EntryContent("HANDOFF.JSON", Encoding.UTF8.GetBytes(recipe.Manifest)));
        }

        WriteArchive(path, entries, recipe.CompressionMethod);
        if (recipe.ArchiveMutation is not null)
        {
            byte[] bytes = File.ReadAllBytes(path);
            recipe.ArchiveMutation(bytes);
            File.WriteAllBytes(path, bytes);
        }
    }

    private static void WriteArchive(
        string path,
        IReadOnlyList<EntryContent> entries,
        CompressionMethod compressionMethod)
    {
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using ZipOutputStream archive = new(file)
        {
            IsStreamOwner = false,
            UseZip64 = UseZip64.Off
        };
        archive.SetLevel(compressionMethod == CompressionMethod.Deflated ? 9 : 0);

        foreach (EntryContent content in entries)
        {
            Crc32 crc = new();
            crc.Update(content.Bytes);
            ZipEntry entry = new(content.Name)
            {
                DateTime = EntryTimestamp,
                CompressionMethod = compressionMethod,
                HostSystem = UnixHostSystem,
                ExternalFileAttributes = RegularFileAttributes,
                IsUnicodeText = true,
                Size = content.Bytes.LongLength,
                Crc = crc.Value
            };

            archive.PutNextEntry(entry);
            archive.Write(content.Bytes);
            archive.CloseEntry();
        }

        archive.Finish();
    }

    private sealed record EntryContent(string Name, byte[] Bytes);
}
