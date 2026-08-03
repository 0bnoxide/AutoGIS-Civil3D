using AutoGIS.Civil3D.Handoff.Validation;
using ICSharpCode.SharpZipLib;

namespace AutoGIS.Civil3D.Handoff.Packaging;

internal sealed class BundleLimitExceededException : IOException
{
    internal BundleLimitExceededException(string entryName, long limit)
        : base($"{entryName} exceeded the {limit}-byte streaming limit.")
    {
        EntryName = entryName;
        Limit = limit;
    }

    internal string EntryName { get; }

    internal long Limit { get; }
}

internal sealed class BundleEntryDataException : IOException
{
    internal BundleEntryDataException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    internal string Code { get; }
}

internal sealed class BoundedReadStream : Stream
{
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    private readonly Stream source;
    private readonly string entryName;
    private readonly long limit;
    private readonly long? expectedSize;
    private readonly long expectedCrc;
    private readonly long compressedSize;
    private long bytesRead;
    private uint runningCrc = uint.MaxValue;
    private bool limitExceeded;
    private bool integrityValidated;

    internal BoundedReadStream(Stream source, string entryName, long limit)
        : this(source, entryName, limit, null, 0, 0)
    {
    }

    internal BoundedReadStream(
        Stream source,
        string entryName,
        long limit,
        long expectedSize,
        long expectedCrc,
        long compressedSize)
        : this(source, entryName, limit, (long?)expectedSize, expectedCrc, compressedSize)
    {
    }

    private BoundedReadStream(
        Stream source,
        string entryName,
        long limit,
        long? expectedSize,
        long expectedCrc,
        long compressedSize)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.entryName = entryName ?? throw new ArgumentNullException(nameof(entryName));
        if (!source.CanRead)
        {
            throw new ArgumentException("The source stream must be readable.", nameof(source));
        }

        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        if (expectedSize is < 0 || expectedCrc is < 0 or > uint.MaxValue || compressedSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSize));
        }

        this.limit = limit;
        this.expectedSize = expectedSize;
        this.expectedCrc = expectedCrc;
        this.compressedSize = compressedSize;
    }

    public override bool CanRead => source.CanRead;

    public override bool CanSeek => source.CanSeek;

    public override bool CanWrite => false;

    public override long Length => source.Length;

    public override long Position
    {
        get => source.Position;
        set => source.Position = value;
    }

    public override void Flush() => throw new NotSupportedException("The bounded stream is read-only.");

    public override int Read(byte[] buffer, int offset, int count)
    {
        ThrowIfLimitExceeded();
        int countRead;
        try
        {
            countRead = source.Read(buffer, offset, CapReadCount(count));
        }
        catch (SharpZipBaseException exception)
        {
            throw CorruptEntry(exception);
        }

        RecordRead(buffer.AsSpan(offset, countRead));
        return countRead;
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfLimitExceeded();
        int countRead;
        try
        {
            countRead = source.Read(buffer[..CapReadCount(buffer.Length)]);
        }
        catch (SharpZipBaseException exception)
        {
            throw CorruptEntry(exception);
        }

        RecordRead(buffer[..countRead]);
        return countRead;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ThrowIfLimitExceeded();
        int countRead;
        try
        {
            countRead = await source.ReadAsync(buffer, offset, CapReadCount(count), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SharpZipBaseException exception)
        {
            throw CorruptEntry(exception);
        }

        RecordRead(buffer.AsSpan(offset, countRead));
        return countRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfLimitExceeded();
        int countRead;
        try
        {
            countRead = await source.ReadAsync(buffer[..CapReadCount(buffer.Length)], cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SharpZipBaseException exception)
        {
            throw CorruptEntry(exception);
        }

        RecordRead(buffer.Span[..countRead]);
        return countRead;
    }

    public override long Seek(long offset, SeekOrigin origin) => source.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException("The bounded stream is read-only.");

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("The bounded stream is read-only.");

    public override void Write(ReadOnlySpan<byte> buffer) =>
        throw new NotSupportedException("The bounded stream is read-only.");

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("The bounded stream is read-only."));

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException("The bounded stream is read-only."));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            source.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RecordRead(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
        {
            ValidateIntegrity();
            return;
        }

        if (bytesRead > limit - bytes.Length)
        {
            bytesRead = limit;
            limitExceeded = true;
            throw new BundleLimitExceededException(entryName, limit);
        }

        bytesRead += bytes.Length;
        foreach (byte value in bytes)
        {
            runningCrc = (runningCrc >> 8) ^ Crc32Table[(runningCrc ^ value) & 0xff];
        }
    }

    private int CapReadCount(int requestedCount)
    {
        long remaining = limit - bytesRead;
        long maximumCount = remaining == long.MaxValue ? long.MaxValue : remaining + 1;
        return (int)Math.Min((long)requestedCount, maximumCount);
    }

    private void ThrowIfLimitExceeded()
    {
        if (limitExceeded)
        {
            throw new BundleLimitExceededException(entryName, limit);
        }
    }

    private void ValidateIntegrity()
    {
        if (expectedSize is null || integrityValidated)
        {
            return;
        }

        integrityValidated = true;
        if (ExceedsCompressionRatio(bytesRead, compressedSize))
        {
            throw new BundleEntryDataException(
                IssueCodes.CompressionRatioExceeded,
                "The ZIP contains an entry with an excessive compression ratio.");
        }

        uint actualCrc = ~runningCrc;
        if (bytesRead != expectedSize.Value || actualCrc != (uint)expectedCrc)
        {
            throw new BundleEntryDataException(
                IssueCodes.InvalidArchive,
                "The ZIP entry data does not match its declared size or CRC.");
        }
    }

    private static bool ExceedsCompressionRatio(long uncompressedSize, long compressedSize)
    {
        if (compressedSize == 0)
        {
            return uncompressedSize > 0;
        }

        return compressedSize <= long.MaxValue / BundleLimits.MaximumCompressionRatio &&
            uncompressedSize > BundleLimits.MaximumCompressionRatio * compressedSize;
    }

    private BundleEntryDataException CorruptEntry(SharpZipBaseException innerException) =>
        new(
            IssueCodes.InvalidArchive,
            $"The ZIP entry data for {entryName} is corrupt.",
            innerException);

    private static uint[] BuildCrc32Table()
    {
        uint[] table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0
                    ? 0xedb88320U ^ (value >> 1)
                    : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
