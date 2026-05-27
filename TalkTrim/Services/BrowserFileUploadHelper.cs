using Microsoft.AspNetCore.Components.Forms;

namespace TalkTrim.Services;

/// <summary>带进度上报的浏览器文件读取与包装。</summary>
public static class BrowserFileUploadHelper
{
    public static async Task CopyToAsync(
        IBrowserFile file,
        Stream destination,
        IProgress<UploadProgressReport>? progress = null,
        int percentStart = 0,
        int percentEnd = 100,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        long total = file.Size;
        if (total <= 0)
        {
            progress?.Report(new UploadProgressReport(percentEnd, message));
            return;
        }

        progress?.Report(new UploadProgressReport(percentStart, message));

        await using var source = file.OpenReadStream(total, cancellationToken);
        var buffer = new byte[81920];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            copied += read;
            var percent = percentStart + (int)(copied * (percentEnd - percentStart) / total);
            progress?.Report(new UploadProgressReport(Math.Clamp(percent, percentStart, percentEnd), message));
        }
    }

    public static IBrowserFile WithProgress(
        IBrowserFile file,
        IProgress<UploadProgressReport> progress,
        int percentStart = 0,
        int percentEnd = 100,
        string? message = null)
        => new ProgressReportingBrowserFile(file, progress, percentStart, percentEnd, message);

    private sealed class ProgressReportingBrowserFile : IBrowserFile
    {
        private readonly IBrowserFile _inner;
        private readonly IProgress<UploadProgressReport> _progress;
        private readonly int _percentStart;
        private readonly int _percentEnd;
        private readonly string? _message;

        public ProgressReportingBrowserFile(
            IBrowserFile inner,
            IProgress<UploadProgressReport> progress,
            int percentStart,
            int percentEnd,
            string? message)
        {
            _inner = inner;
            _progress = progress;
            _percentStart = percentStart;
            _percentEnd = percentEnd;
            _message = message;
        }

        public string Name => _inner.Name;

        public DateTimeOffset LastModified => _inner.LastModified;

        public long Size => _inner.Size;

        public string ContentType => _inner.ContentType;

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            var stream = _inner.OpenReadStream(maxAllowedSize, cancellationToken);
            return new ProgressStream(stream, Size, _progress, _percentStart, _percentEnd, _message);
        }
    }

    private sealed class ProgressStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _totalBytes;
        private readonly IProgress<UploadProgressReport> _progress;
        private readonly int _percentStart;
        private readonly int _percentEnd;
        private readonly string? _message;
        private long _transferred;

        public ProgressStream(
            Stream inner,
            long totalBytes,
            IProgress<UploadProgressReport> progress,
            int percentStart,
            int percentEnd,
            string? message)
        {
            _inner = inner;
            _totalBytes = totalBytes;
            _progress = progress;
            _percentStart = percentStart;
            _percentEnd = percentEnd;
            _message = message;
        }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => _inner.CanSeek;

        public override bool CanWrite => _inner.CanWrite;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            ReportProgress(read);
            return read;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken);
            ReportProgress(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            ReportProgress(read);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void ReportProgress(int bytesRead)
        {
            if (bytesRead <= 0 || _totalBytes <= 0)
            {
                return;
            }

            _transferred += bytesRead;
            var percent = _percentStart + (int)(_transferred * (_percentEnd - _percentStart) / _totalBytes);
            _progress.Report(new UploadProgressReport(Math.Clamp(percent, _percentStart, _percentEnd), _message));
        }
    }
}
