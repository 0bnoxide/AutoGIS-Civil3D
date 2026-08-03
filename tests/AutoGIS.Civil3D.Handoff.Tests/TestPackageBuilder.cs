using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AutoGIS.Civil3D.Handoff.Manifest;
using ICSharpCode.SharpZipLib.Zip;

namespace AutoGIS.Civil3D.Handoff.Tests;

public enum PackageFault
{
    Valid,
    ValidLocalExtraField,
    MissingSurface,
    ExtraEntry,
    UnsafePath,
    WindowsRootedPath,
    CaseCollision,
    DirectoryEntry,
    SymlinkEntry,
    NonUnixHostSymlink,
    DosReparseEntry,
    DosDeviceEntry,
    EncryptedSurface,
    UnsupportedCompression,
    ManifestTooLarge,
    SurfaceTooLarge,
    CompressionRatioExceeded,
    LocalHeaderFlagsMismatch,
    LocalHeaderCompressionMismatch,
    LocalHeaderNameMismatch,
    LocalHeaderVersionMismatch,
    LocalHeaderCrcMismatch,
    LocalHeaderSizeMismatch,
    LocalHeaderMismatchBeforeUnsafeName,
    LegacyEncodedUnexpectedEntry,
    UnderreportedManifestSize,
    MatchingBadManifestCrc,
    OverreportedCompressedSize,
    CompressionRatioBypass,
    CorruptDeflatedSurface,
    Malformed
}

internal static class TestPackageBuilder
{
    private const uint CentralDirectoryHeaderSignature = 0x02014b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const ushort UnixMadeBy = 0x0314;
    private const ushort MsdosMadeBy = 0x0014;
    private const uint RegularFileAttributes = 0x81a40000;
    private const uint DirectoryAttributes = 0x41ed0000;
    private const uint SymbolicLinkAttributes = 0xa1ff0000;
    private const ushort BZip2CompressionMethod = 12;
    private const long ManifestLimit = 1L * 1024 * 1024;
    private const long SurfaceLimit = 2L * 1024 * 1024 * 1024;

    internal static string Create(PackageFault fault)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "AutoGIS.Civil3D.Handoff.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "package.zip");
        if (fault == PackageFault.Malformed)
        {
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("not a ZIP archive"));
            return path;
        }

        if (fault == PackageFault.ValidLocalExtraField)
        {
            CreateArchiveWithLocalExtraField(path);
            return path;
        }

        List<EntrySpec> entries =
        [
            new("handoff.json", "{}"u8.ToArray()),
            new("surface.landxml", "<LandXML/>"u8.ToArray())
        ];
        CompressionLevel compressionLevel = CompressionLevel.NoCompression;

        if (fault is PackageFault.OverreportedCompressedSize or PackageFault.CompressionRatioBypass)
        {
            string ratioSurface = TestLandXml.Valid.Replace(
                "<LandXML ",
                $"<!--{new string('A', 2_000_000)}-->\n<LandXML ",
                StringComparison.Ordinal);
            entries =
            [
                new("handoff.json", Encoding.UTF8.GetBytes(CreateManifest(ratioSurface))),
                new("surface.landxml", Encoding.UTF8.GetBytes(ratioSurface))
            ];
            compressionLevel = CompressionLevel.SmallestSize;
        }
        else if (fault == PackageFault.CorruptDeflatedSurface)
        {
            entries =
            [
                new("handoff.json", Encoding.UTF8.GetBytes(CreateManifest(TestLandXml.Valid))),
                new("surface.landxml", Encoding.UTF8.GetBytes(TestLandXml.Valid))
            ];
            compressionLevel = CompressionLevel.SmallestSize;
        }

        switch (fault)
        {
            case PackageFault.Valid:
                break;
            case PackageFault.MissingSurface:
                entries.RemoveAt(1);
                break;
            case PackageFault.ExtraEntry:
            case PackageFault.LegacyEncodedUnexpectedEntry:
                entries.Add(new EntrySpec("unexpected.txt", "unexpected"u8.ToArray()));
                break;
            case PackageFault.UnsafePath:
                entries[1] = new EntrySpec("../surface.landxml", "<LandXML/>"u8.ToArray());
                break;
            case PackageFault.WindowsRootedPath:
                entries[1] = new EntrySpec("C:/surface.landxml", "<LandXML/>"u8.ToArray());
                break;
            case PackageFault.CaseCollision:
                entries.Add(new EntrySpec("HANDOFF.JSON", "{}"u8.ToArray()));
                break;
            case PackageFault.DirectoryEntry:
            case PackageFault.SymlinkEntry:
            case PackageFault.NonUnixHostSymlink:
            case PackageFault.DosReparseEntry:
            case PackageFault.DosDeviceEntry:
            case PackageFault.EncryptedSurface:
            case PackageFault.UnsupportedCompression:
            case PackageFault.ManifestTooLarge:
            case PackageFault.SurfaceTooLarge:
            case PackageFault.CompressionRatioExceeded:
            case PackageFault.LocalHeaderFlagsMismatch:
            case PackageFault.LocalHeaderCompressionMismatch:
            case PackageFault.LocalHeaderNameMismatch:
            case PackageFault.LocalHeaderVersionMismatch:
            case PackageFault.LocalHeaderCrcMismatch:
            case PackageFault.LocalHeaderSizeMismatch:
            case PackageFault.UnderreportedManifestSize:
            case PackageFault.MatchingBadManifestCrc:
            case PackageFault.OverreportedCompressedSize:
            case PackageFault.CompressionRatioBypass:
            case PackageFault.CorruptDeflatedSurface:
                break;
            case PackageFault.LocalHeaderMismatchBeforeUnsafeName:
                entries[1] = new EntrySpec("../surface.landxml", "<LandXML/>"u8.ToArray());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }

        CreateArchive(path, entries, compressionLevel);
        ApplyFault(path, fault);
        return path;
    }

    internal static string CreateValid(VerticalDatumStatus datumStatus) =>
        CreateBundle(CreateManifest(TestLandXml.Valid, datumStatus), TestLandXml.Valid);

    internal static string CreateBundle(string manifest, string landXml)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "AutoGIS.Civil3D.Handoff.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, "package.zip");
        CreateArchive(
            path,
            [
                new EntrySpec("handoff.json", Encoding.UTF8.GetBytes(manifest)),
                new EntrySpec("surface.landxml", Encoding.UTF8.GetBytes(landXml))
            ]);
        return path;
    }

    internal static string CreateManifest(
        string landXml,
        VerticalDatumStatus datumStatus = VerticalDatumStatus.Known)
    {
        string sha256 = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(landXml))).ToLowerInvariant();
        string source = datumStatus == VerticalDatumStatus.Known
            ? TestManifests.KnownDatum
            : TestManifests.UnknownDatum;

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

    internal static void Delete(string packagePath)
    {
        string directory = Path.GetDirectoryName(packagePath)
            ?? throw new ArgumentException("The package path must have a directory.", nameof(packagePath));
        Directory.Delete(directory, recursive: true);
    }

    private static void CreateArchive(
        string path,
        IReadOnlyList<EntrySpec> entries,
        CompressionLevel compressionLevel = CompressionLevel.NoCompression)
    {
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(file, ZipArchiveMode.Create, leaveOpen: false, Encoding.UTF8);

        foreach (EntrySpec spec in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(spec.Name, compressionLevel);
            entry.LastWriteTime = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);
            using Stream entryStream = entry.Open();
            entryStream.Write(spec.Contents);
        }

        archive.Dispose();

        byte[] bytes = File.ReadAllBytes(path);
        foreach (EntrySpec spec in entries)
        {
            PatchEntry(bytes, spec.Name, (centralHeaderOffset, _) =>
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(centralHeaderOffset + 4),
                    UnixMadeBy);
                BinaryPrimitives.WriteUInt32LittleEndian(
                    bytes.AsSpan(centralHeaderOffset + 38),
                    RegularFileAttributes);
            });
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void CreateArchiveWithLocalExtraField(string path)
    {
        ReadOnlySpan<byte> extendedTimestampExtraField =
        [
            0x55, 0x54,
            0x05, 0x00,
            0x01,
            0x00, 0x00, 0x00, 0x00
        ];
        EntrySpec[] entries =
        [
            new("handoff.json", "{}"u8.ToArray()),
            new("surface.landxml", "<LandXML/>"u8.ToArray())
        ];

        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using ZipOutputStream archive = new(file);
        archive.UseZip64 = UseZip64.Off;
        archive.SetLevel(0);
        foreach (EntrySpec spec in entries)
        {
            ZipEntry entry = new(spec.Name)
            {
                DateTime = new DateTime(2026, 8, 2, 0, 0, 0, DateTimeKind.Utc),
                ExtraData = spec.Name == "handoff.json"
                    ? extendedTimestampExtraField.ToArray()
                    : []
            };
            archive.PutNextEntry(entry);
            archive.Write(spec.Contents);
            archive.CloseEntry();
        }

        archive.Finish();
    }

    private static void ApplyFault(string path, PackageFault fault)
    {
        if (fault is PackageFault.Valid or PackageFault.MissingSurface or PackageFault.ExtraEntry
            or PackageFault.UnsafePath or PackageFault.WindowsRootedPath or PackageFault.CaseCollision)
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        switch (fault)
        {
            case PackageFault.DirectoryEntry:
                PatchEntry(bytes, "handoff.json", (centralHeaderOffset, _) =>
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 38),
                        DirectoryAttributes));
                break;
            case PackageFault.SymlinkEntry:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, _) =>
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 38),
                        SymbolicLinkAttributes));
                break;
            case PackageFault.NonUnixHostSymlink:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, _) =>
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 4),
                        MsdosMadeBy);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 38),
                        SymbolicLinkAttributes);
                });
                break;
            case PackageFault.DosReparseEntry:
                SetDosAttributes(bytes, "surface.landxml", (uint)FileAttributes.ReparsePoint);
                break;
            case PackageFault.DosDeviceEntry:
                SetDosAttributes(bytes, "surface.landxml", (uint)FileAttributes.Device);
                break;
            case PackageFault.EncryptedSurface:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    SetEncryptedFlag(bytes, centralHeaderOffset + 8);
                    SetEncryptedFlag(bytes, localHeaderOffset + 6);
                });
                break;
            case PackageFault.UnsupportedCompression:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 10),
                        BZip2CompressionMethod);
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 8),
                        BZip2CompressionMethod);
                });
                break;
            case PackageFault.ManifestTooLarge:
                PatchDeclaredSize(bytes, "handoff.json", checked((uint)(ManifestLimit + 1)));
                break;
            case PackageFault.SurfaceTooLarge:
                PatchDeclaredSize(bytes, "surface.landxml", checked((uint)(SurfaceLimit + 1)));
                break;
            case PackageFault.CompressionRatioExceeded:
                PatchCompressionRatio(bytes, "surface.landxml");
                break;
            case PackageFault.LocalHeaderFlagsMismatch:
                PatchEntry(bytes, "surface.landxml", (_, localHeaderOffset) =>
                {
                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 6));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 6),
                        (ushort)(flags ^ 0x0001));
                });
                break;
            case PackageFault.LocalHeaderCompressionMismatch:
                PatchEntry(bytes, "surface.landxml", (_, localHeaderOffset) =>
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 8),
                        BZip2CompressionMethod));
                break;
            case PackageFault.LocalHeaderNameMismatch:
                PatchEntry(bytes, "surface.landxml", (_, localHeaderOffset) =>
                {
                    byte[] replacement = "syrface.landxml"u8.ToArray();
                    ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 26));
                    if (replacement.Length != nameLength)
                    {
                        throw new InvalidDataException("The replacement local entry name must preserve its length.");
                    }

                    replacement.CopyTo(bytes, localHeaderOffset + 30);
                });
                break;
            case PackageFault.LocalHeaderVersionMismatch:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    ushort version = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 6));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 4),
                        checked((ushort)(version + 1)));
                });
                break;
            case PackageFault.LocalHeaderCrcMismatch:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    CopyCentralMetadataToLocalHeader(bytes, centralHeaderOffset, localHeaderOffset);
                    uint crc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(localHeaderOffset + 14));
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 14),
                        crc ^ 0x00000001);
                });
                break;
            case PackageFault.LocalHeaderSizeMismatch:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    CopyCentralMetadataToLocalHeader(bytes, centralHeaderOffset, localHeaderOffset);
                    uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 22));
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 22),
                        checked(uncompressedSize + 1));
                });
                break;
            case PackageFault.LocalHeaderMismatchBeforeUnsafeName:
                PatchEntry(bytes, "../surface.landxml", (_, localHeaderOffset) =>
                {
                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 6));
                    BinaryPrimitives.WriteUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 6),
                        (ushort)(flags ^ 0x0001));
                });
                break;
            case PackageFault.LegacyEncodedUnexpectedEntry:
                PatchEntry(bytes, "unexpected.txt", (centralHeaderOffset, localHeaderOffset) =>
                {
                    ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 8));
                    flags = (ushort)(flags & ~0x0800);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(centralHeaderOffset + 8), flags);
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(localHeaderOffset + 6), flags);

                    byte[] legacyName = new byte[14];
                    "legacy-entry-"u8.CopyTo(legacyName);
                    legacyName[^1] = 0x82;
                    ReplaceEntryNames(bytes, centralHeaderOffset, localHeaderOffset, legacyName);
                });
                break;
            case PackageFault.UnderreportedManifestSize:
                PatchDeclaredSize(bytes, "handoff.json", 1);
                break;
            case PackageFault.MatchingBadManifestCrc:
                PatchEntry(bytes, "handoff.json", (centralHeaderOffset, localHeaderOffset) =>
                {
                    uint crc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(centralHeaderOffset + 16));
                    uint badCrc = crc ^ 0x00000001;
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(centralHeaderOffset + 16), badCrc);
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(localHeaderOffset + 14), badCrc);
                });
                break;
            case PackageFault.OverreportedCompressedSize:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 20));
                    uint overreportedSize = checked(compressedSize + 32_768);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 20),
                        overreportedSize);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 18),
                        overreportedSize);
                });
                break;
            case PackageFault.CompressionRatioBypass:
                PatchEntry(bytes, "surface.landxml", (centralHeaderOffset, localHeaderOffset) =>
                {
                    uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 20));
                    uint underreportedSize = checked(compressedSize * 100);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(centralHeaderOffset + 24),
                        underreportedSize);
                    BinaryPrimitives.WriteUInt32LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 22),
                        underreportedSize);
                });
                break;
            case PackageFault.CorruptDeflatedSurface:
                PatchEntry(bytes, "surface.landxml", (_, localHeaderOffset) =>
                {
                    ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 26));
                    ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(
                        bytes.AsSpan(localHeaderOffset + 28));
                    int dataOffset = checked(localHeaderOffset + 30 + nameLength + extraLength);
                    bytes[dataOffset] = (byte)((bytes[dataOffset] & 0xf8) | 0x07);
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
        }

        File.WriteAllBytes(path, bytes);
    }

    private static void SetDosAttributes(byte[] bytes, string entryName, uint attributes)
    {
        PatchEntry(bytes, entryName, (centralHeaderOffset, _) =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                bytes.AsSpan(centralHeaderOffset + 4),
                MsdosMadeBy);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(centralHeaderOffset + 38),
                attributes);
        });
    }

    private static void CopyCentralMetadataToLocalHeader(
        byte[] bytes,
        int centralHeaderOffset,
        int localHeaderOffset)
    {
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(centralHeaderOffset + 8));
        flags = (ushort)(flags & ~0x0008);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(centralHeaderOffset + 8), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(localHeaderOffset + 6), flags);

        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(centralHeaderOffset + 16));
        uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(centralHeaderOffset + 20));
        uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(centralHeaderOffset + 24));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(localHeaderOffset + 14), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(localHeaderOffset + 18), compressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(localHeaderOffset + 22), uncompressedSize);
    }

    private static void PatchDeclaredSize(byte[] bytes, string entryName, uint uncompressedSize)
    {
        PatchEntry(bytes, entryName, (centralHeaderOffset, localHeaderOffset) =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(centralHeaderOffset + 24),
                uncompressedSize);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(localHeaderOffset + 22),
                uncompressedSize);
        });
    }

    private static void PatchCompressionRatio(byte[] bytes, string entryName)
    {
        PatchEntry(bytes, entryName, (centralHeaderOffset, localHeaderOffset) =>
        {
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(centralHeaderOffset + 20));
            uint uncompressedSize = checked((compressedSize * 100) + 1);

            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(centralHeaderOffset + 24),
                uncompressedSize);
            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(localHeaderOffset + 22),
                uncompressedSize);
        });
    }

    private static void SetEncryptedFlag(byte[] bytes, int offset)
    {
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), (ushort)(flags | 1));
    }

    private static void ReplaceEntryNames(
        byte[] bytes,
        int centralHeaderOffset,
        int localHeaderOffset,
        byte[] replacement)
    {
        ushort centralNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(centralHeaderOffset + 28));
        ushort localNameLength = BinaryPrimitives.ReadUInt16LittleEndian(
            bytes.AsSpan(localHeaderOffset + 26));
        if (replacement.Length != centralNameLength || replacement.Length != localNameLength)
        {
            throw new InvalidDataException("The replacement entry name must preserve both header name lengths.");
        }

        replacement.CopyTo(bytes, centralHeaderOffset + 46);
        replacement.CopyTo(bytes, localHeaderOffset + 30);
    }

    private static void PatchEntry(
        byte[] bytes,
        string entryName,
        Action<int, int> patch)
    {
        for (int offset = 0; offset <= bytes.Length - 46; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) != CentralDirectoryHeaderSignature)
            {
                continue;
            }

            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 28));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 30));
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 32));
            int headerLength = checked(46 + nameLength + extraLength + commentLength);
            if (offset + headerLength > bytes.Length)
            {
                throw new InvalidDataException("The test ZIP central directory is truncated.");
            }

            string currentName = Encoding.UTF8.GetString(bytes, offset + 46, nameLength);
            if (!string.Equals(currentName, entryName, StringComparison.Ordinal))
            {
                offset += headerLength - 1;
                continue;
            }

            int localHeaderOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
                bytes.AsSpan(offset + 42)));
            if (localHeaderOffset < 0
                || localHeaderOffset > bytes.Length - 30
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(localHeaderOffset)) != LocalFileHeaderSignature)
            {
                throw new InvalidDataException("The test ZIP local header is invalid.");
            }

            patch(offset, localHeaderOffset);
            return;
        }

        throw new InvalidDataException($"The test ZIP does not contain '{entryName}'.");
    }

    private sealed record EntrySpec(string Name, byte[] Contents);
}
