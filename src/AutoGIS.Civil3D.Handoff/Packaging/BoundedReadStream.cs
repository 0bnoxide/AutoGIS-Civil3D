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

internal sealed class BoundedReadStream : Stream
{
    private readonly Stream source;
    private readonly string entryName;
    private readonly long limit;
    private long bytesRead;
    private bool limitExceeded;

    internal BoundedReadStream(Stream source, string entryName, long limit)
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

        this.limit = limit;
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
        int countRead = source.Read(buffer, offset, CapReadCount(count));
        RecordRead(countRead);
        return countRead;
    }

    public override int Read(Span<byte> buffer)
    {
        ThrowIfLimitExceeded();
        int countRead = source.Read(buffer[..CapReadCount(buffer.Length)]);
        RecordRead(countRead);
        return countRead;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ThrowIfLimitExceeded();
        int countRead = await source.ReadAsync(buffer, offset, CapReadCount(count), cancellationToken)
            .ConfigureAwait(false);
        RecordRead(countRead);
        return countRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfLimitExceeded();
        int countRead = await source.ReadAsync(buffer[..CapReadCount(buffer.Length)], cancellationToken)
            .ConfigureAwait(false);
        RecordRead(countRead);
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

    private void RecordRead(int countRead)
    {
        if (countRead == 0)
        {
            return;
        }

        if (bytesRead > limit - countRead)
        {
            bytesRead = limit;
            limitExceeded = true;
            throw new BundleLimitExceededException(entryName, limit);
        }

        bytesRead += countRead;
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
}
