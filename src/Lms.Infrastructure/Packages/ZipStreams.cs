using Lms.Shared.Packages;

namespace Lms.Infrastructure.Packages;

/// <summary>Reads at most <c>length</c> bytes from the current position of a seekable stream and never closes it.</summary>
internal sealed class BoundedReadStream(Stream inner, long limit) : Stream
{
    private readonly long _limit = limit;
    private long _remaining = limit;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _limit;

    public override long Position
    {
        get => _limit - _remaining;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_remaining <= 0)
        {
            return 0;
        }

        var read = inner.Read(buffer[..(int)Math.Min(buffer.Length, _remaining)]);
        _remaining -= read;
        return read;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_remaining <= 0)
        {
            return 0;
        }

        var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, _remaining)], cancellationToken);
        _remaining -= read;
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// The stream a sink reads during extraction: exactly <c>expected</c> bytes (the real size counted by the inspection). More bytes than
/// that throw <see cref="ZipHardeningException"/>; fewer is detected by <see cref="EnsureCompleteAsync"/>.
/// </summary>
internal sealed class ExactLengthStream(Stream inner, long expected) : Stream
{
    private long _read;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => expected;

    public override long Position
    {
        get => _read;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        return Count(read);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    /// <summary>Drains what the sink left unread (it may stop at its declared length) and checks that the entry had exactly the inspected size.</summary>
    public async Task EnsureCompleteAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (await ReadAsync(buffer, ct) > 0)
        {
        }

        if (_read != expected)
        {
            throw new ZipHardeningException(PackageRejectReasons.invalid_archive, "An entry changed size between the inspection and the extraction.");
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read)
    {
        _read += read;
        if (_read > expected)
        {
            throw new ZipHardeningException(PackageRejectReasons.too_large, "An entry grew between the inspection and the extraction.");
        }

        return read;
    }
}
