using System.Buffers.Binary;
using System.Text;
using AutoGIS.Civil3D.Handoff.Packaging;
using Xunit;

namespace AutoGIS.Civil3D.Handoff.Tests;

public sealed class BundleArchiveTests
{
    [Fact]
    public void Valid_two_entry_archive_exposes_bounded_manifest_and_surface_streams()
    {
        string path = TestPackageBuilder.Create(PackageFault.Valid);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            Assert.Empty(result.Issues);
            using BundleArchive archive = Assert.IsType<BundleArchive>(result.Archive);
            Assert.Equal("{}", Encoding.UTF8.GetString(archive.ReadManifestBytes()));

            using Stream surfaceStream = archive.OpenSurfaceStream();
            using StreamReader reader = new(surfaceStream, Encoding.UTF8);
            Assert.Equal("<LandXML/>", reader.ReadToEnd());
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Valid_archive_with_a_standard_local_extra_field_is_accepted()
    {
        string path = TestPackageBuilder.Create(PackageFault.ValidLocalExtraField);
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(0x04034b50U, BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(26));
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(28));
            int extraOffset = 30 + nameLength;
            Assert.True(extraLength >= 9);
            Assert.Equal(0x5455, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(extraOffset)));

            using (System.IO.Compression.ZipArchive standardArchive =
                System.IO.Compression.ZipFile.OpenRead(path))
            {
                Assert.Equal(2, standardArchive.Entries.Count);
                using Stream standardManifest = standardArchive.GetEntry("handoff.json")!.Open();
                using StreamReader standardReader = new(standardManifest, Encoding.UTF8);
                Assert.Equal("{}", standardReader.ReadToEnd());
            }

            BundleOpenResult result = BundleArchive.Open(path);

            Assert.Empty(result.Issues);
            using BundleArchive archive = Assert.IsType<BundleArchive>(result.Archive);
            Assert.Equal("{}", Encoding.UTF8.GetString(archive.ReadManifestBytes()));
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Theory]
    [InlineData(PackageFault.ValidDataDescriptor)]
    [InlineData(PackageFault.ValidZip64)]
    public void Valid_descriptor_and_zip64_archives_are_accepted(PackageFault fault)
    {
        string path = TestPackageBuilder.Create(fault);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            Assert.Empty(result.Issues);
            using BundleArchive archive = Assert.IsType<BundleArchive>(result.Archive);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Theory]
    [InlineData(PackageFault.MissingSurface, "ZIP003")]
    [InlineData(PackageFault.ExtraEntry, "ZIP004")]
    [InlineData(PackageFault.UnsafePath, "ZIP005")]
    [InlineData(PackageFault.WindowsRootedPath, "ZIP005")]
    [InlineData(PackageFault.CaseCollision, "ZIP006")]
    [InlineData(PackageFault.EncryptedSurface, "ZIP008")]
    [InlineData(PackageFault.UnsupportedCompression, "ZIP009")]
    public void Invalid_container_returns_stable_primary_code(
        PackageFault fault,
        string expectedCode)
    {
        string path = TestPackageBuilder.Create(fault);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            Assert.Null(result.Archive);
            Assert.Equal(expectedCode, Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Theory]
    [InlineData(PackageFault.DirectoryEntry)]
    [InlineData(PackageFault.SymlinkEntry)]
    [InlineData(PackageFault.NonUnixHostSymlink)]
    [InlineData(PackageFault.DosReparseEntry)]
    [InlineData(PackageFault.DosDeviceEntry)]
    public void Non_regular_entry_returns_zip007(PackageFault fault)
    {
        string path = TestPackageBuilder.Create(fault);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            result.Archive?.Dispose();
            Assert.Null(result.Archive);
            Assert.Equal("ZIP007", Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Theory]
    [InlineData(PackageFault.ManifestTooLarge, "ZIP010")]
    [InlineData(PackageFault.SurfaceTooLarge, "ZIP011")]
    public void Entry_with_declared_size_over_limit_returns_stable_code(
        PackageFault fault,
        string expectedCode)
    {
        string path = TestPackageBuilder.Create(fault);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            Assert.Null(result.Archive);
            Assert.Equal(expectedCode, Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Entry_with_excessive_declared_compression_ratio_returns_zip012()
    {
        string path = TestPackageBuilder.Create(PackageFault.CompressionRatioExceeded);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            Assert.Null(result.Archive);
            Assert.Equal("ZIP012", Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Malformed_archive_returns_zip001()
    {
        string path = TestPackageBuilder.Create(PackageFault.Malformed);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            result.Archive?.Dispose();
            Assert.Null(result.Archive);
            Assert.Equal("ZIP001", Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Theory]
    [InlineData(PackageFault.LocalHeaderFlagsMismatch)]
    [InlineData(PackageFault.LocalHeaderCompressionMismatch)]
    [InlineData(PackageFault.LocalHeaderNameMismatch)]
    [InlineData(PackageFault.LocalHeaderVersionMismatch)]
    [InlineData(PackageFault.LocalHeaderCrcMismatch)]
    [InlineData(PackageFault.LocalHeaderSizeMismatch)]
    public void Central_and_local_header_mismatch_returns_zip001(PackageFault fault)
    {
        string path = TestPackageBuilder.Create(fault);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            result.Archive?.Dispose();
            Assert.Null(result.Archive);
            Assert.Equal("ZIP001", Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Theory]
    [InlineData(PackageFault.DataDescriptorMismatch)]
    [InlineData(PackageFault.Zip64LocatorMismatch)]
    public void Invalid_descriptor_or_zip64_locator_returns_zip001(PackageFault fault)
    {
        string path = TestPackageBuilder.Create(fault);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            result.Archive?.Dispose();
            Assert.Null(result.Archive);
            Assert.Equal("ZIP001", Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Legacy_encoded_unexpected_entry_returns_zip004()
    {
        string path = TestPackageBuilder.Create(PackageFault.LegacyEncodedUnexpectedEntry);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            result.Archive?.Dispose();
            Assert.Null(result.Archive);
            Assert.Equal("ZIP004", Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Local_header_mismatch_precedes_unsafe_entry_name()
    {
        string path = TestPackageBuilder.Create(PackageFault.LocalHeaderMismatchBeforeUnsafeName);
        try
        {
            BundleOpenResult result = BundleArchive.Open(path);

            result.Archive?.Dispose();
            Assert.Null(result.Archive);
            Assert.Equal("ZIP001", Assert.Single(result.Issues).Code);
        }
        finally
        {
            TestPackageBuilder.Delete(path);
        }
    }

    [Fact]
    public void Bounded_stream_rejects_synchronous_read_past_runtime_limit()
    {
        using BoundedReadStream stream = new(
            new MemoryStream([1, 2, 3, 4, 5]),
            "surface.landxml",
            4);
        byte[] buffer = new byte[4];

        Assert.Equal(4, stream.Read(buffer, 0, buffer.Length));
        BundleLimitExceededException exception = Assert.Throws<BundleLimitExceededException>(
            () => stream.Read(new byte[1], 0, 1));

        Assert.Equal("surface.landxml", exception.EntryName);
        Assert.Equal(4, exception.Limit);
    }

    [Fact]
    public async Task Bounded_stream_rejects_asynchronous_read_past_runtime_limit()
    {
        using BoundedReadStream stream = new(
            new MemoryStream([1, 2, 3, 4, 5]),
            "surface.landxml",
            4);
        byte[] buffer = new byte[4];

        Assert.Equal(4, await stream.ReadAsync(buffer.AsMemory()));
        BundleLimitExceededException exception = await Assert.ThrowsAsync<BundleLimitExceededException>(
            async () => await stream.ReadAsync(new byte[1].AsMemory()));

        Assert.Equal("surface.landxml", exception.EntryName);
        Assert.Equal(4, exception.Limit);
    }

    [Fact]
    public void Caller_requested_zero_length_synchronous_reads_do_not_finalize_integrity()
    {
        using BoundedReadStream stream = new(
            new MemoryStream([1, 2, 3]),
            "handoff.json",
            3,
            3,
            0x55bc801dL,
            3);
        byte[] buffer = new byte[3];

        Assert.Equal(0, stream.Read(buffer, 0, 0));
        Assert.Equal(0, stream.Read(Span<byte>.Empty));
        Assert.Equal(3, stream.Read(buffer.AsSpan()));
        Assert.Equal([1, 2, 3], buffer);
        Assert.Equal(0, stream.Read(buffer.AsSpan(0, 1)));
    }

    [Fact]
    public async Task Caller_requested_zero_length_asynchronous_reads_do_not_finalize_integrity()
    {
        using BoundedReadStream stream = new(
            new MemoryStream([1, 2, 3]),
            "handoff.json",
            3,
            3,
            0x55bc801dL,
            3);
        byte[] buffer = new byte[3];

        Assert.Equal(0, await stream.ReadAsync(buffer, 0, 0, CancellationToken.None));
        Assert.Equal(0, await stream.ReadAsync(Memory<byte>.Empty));
        Assert.Equal(3, await stream.ReadAsync(buffer, 0, buffer.Length, CancellationToken.None));
        Assert.Equal([1, 2, 3], buffer);
        Assert.Equal(0, await stream.ReadAsync(buffer.AsMemory(0, 1)));
    }

    [Fact]
    public void Bounded_stream_caps_oversized_synchronous_first_read_and_faults_retries()
    {
        using RecordingMemoryStream source = new([1, 2, 3, 4, 5, 6, 7]);
        using BoundedReadStream stream = new(source, "surface.landxml", 4);

        Assert.Throws<BundleLimitExceededException>(() => stream.Read(new byte[12], 0, 12));
        Assert.Equal(5, source.MaximumSynchronousRequest);
        int readAttempts = source.SynchronousReadAttempts;

        Assert.Throws<BundleLimitExceededException>(() => stream.Read(new byte[1], 0, 1));
        Assert.Equal(readAttempts, source.SynchronousReadAttempts);
    }

    [Fact]
    public async Task Bounded_stream_caps_oversized_asynchronous_first_read_and_faults_retries()
    {
        using RecordingMemoryStream source = new([1, 2, 3, 4, 5, 6, 7]);
        using BoundedReadStream stream = new(source, "surface.landxml", 4);

        await Assert.ThrowsAsync<BundleLimitExceededException>(
            async () => await stream.ReadAsync(new byte[12].AsMemory()));
        Assert.Equal(5, source.MaximumAsynchronousRequest);
        int readAttempts = source.AsynchronousReadAttempts;

        await Assert.ThrowsAsync<BundleLimitExceededException>(
            async () => await stream.ReadAsync(new byte[1].AsMemory()));
        Assert.Equal(readAttempts, source.AsynchronousReadAttempts);
    }

    [Fact]
    public void Bounded_stream_delegates_seek_state_and_rejects_writes()
    {
        using BoundedReadStream stream = new(
            new MemoryStream([1, 2, 3]),
            "handoff.json",
            10);

        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.False(stream.CanWrite);
        Assert.Equal(2, stream.Seek(2, SeekOrigin.Begin));
        Assert.Equal(2, stream.Position);
        Assert.Throws<NotSupportedException>(() => stream.Write([4], 0, 1));
    }

    private sealed class RecordingMemoryStream : MemoryStream
    {
        internal RecordingMemoryStream(byte[] buffer)
            : base(buffer, writable: false)
        {
        }

        internal int SynchronousReadAttempts { get; private set; }

        internal int MaximumSynchronousRequest { get; private set; }

        internal int AsynchronousReadAttempts { get; private set; }

        internal int MaximumAsynchronousRequest { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            SynchronousReadAttempts++;
            MaximumSynchronousRequest = Math.Max(MaximumSynchronousRequest, count);
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            AsynchronousReadAttempts++;
            MaximumAsynchronousRequest = Math.Max(MaximumAsynchronousRequest, buffer.Length);
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
