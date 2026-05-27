using Aliyun.OSS;
using Aliyun.OSS.Common;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;

namespace TalkTrim.Services;

/// <summary>
/// 将浏览器文件上传到阿里云 OSS，并返回签名 URL（逻辑参考 Youtube_Learner app/electron/services/oss.ts）。
/// </summary>
public sealed class OssUploadService
{
    private readonly OssOptions _options;
    private readonly ILogger<OssUploadService> _logger;

    public OssUploadService(IOptions<OssOptions> options, ILogger<OssUploadService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool IsEnabled =>
        _options.Enabled
        && !string.IsNullOrWhiteSpace(_options.Bucket)
        && !string.IsNullOrWhiteSpace(_options.AccessKeyId)
        && !string.IsNullOrWhiteSpace(_options.AccessKeySecret)
        && !string.IsNullOrWhiteSpace(_options.Region);

    public Task<string> UploadBrowserFileAsync(
        IBrowserFile file,
        CancellationToken cancellationToken = default)
        => UploadBrowserFileAsync(file, progress: null, cancellationToken);

    public async Task<string> UploadBrowserFileAsync(
        IBrowserFile file,
        IProgress<UploadProgressReport>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("OSS 未启用或配置不完整。");
        }

        if (file.Size <= 0)
        {
            throw new InvalidOperationException("文件内容不能为空。");
        }

        string extension = Path.GetExtension(file.Name);
        string objectKey = BuildObjectKey(extension);
        string tempPath = Path.Combine(Path.GetTempPath(), $"oss-upload-{Guid.NewGuid():N}{extension}");
        var client = CreateClient();

        try
        {
            await using (FileStream tempFile = File.Create(tempPath))
            {
                await BrowserFileUploadHelper.CopyToAsync(
                    file,
                    tempFile,
                    progress,
                    percentStart: 0,
                    percentEnd: 45,
                    message: "正在接收视频文件…",
                    cancellationToken);
            }

            // Blazor 的 BrowserFileStream 不支持同步读，OSS SDK 的 PutObject(Stream) 会触发该错误。
            await Task.Run(
                () => PutLocalFileWithProgress(client, objectKey, tempPath, progress),
                cancellationToken);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        progress?.Report(new UploadProgressReport(100, "上传完成"));

        var expireUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, _options.SignedUrlExpireSeconds));
        var signedUri = client.GeneratePresignedUri(_options.Bucket, objectKey, expireUtc);
        string url = signedUri.ToString();

        _logger.LogInformation(
            "OSS 上传成功：Bucket={Bucket}, ObjectKey={ObjectKey}, Size={Size}",
            _options.Bucket,
            objectKey,
            file.Size);

        return url;
    }

    private void PutLocalFileWithProgress(
        OssClient client,
        string objectKey,
        string localFilePath,
        IProgress<UploadProgressReport>? progress)
    {
        using var fileStream = File.OpenRead(localFilePath);
        var request = new PutObjectRequest(_options.Bucket, objectKey, fileStream);
        request.StreamTransferProgress += (_, args) =>
        {
            if (args.TotalBytes <= 0)
            {
                return;
            }

            var percent = 45 + (int)(args.TransferredBytes * 54 / args.TotalBytes);
            progress?.Report(new UploadProgressReport(Math.Clamp(percent, 45, 99), "正在上传到 OSS…"));
        };

        client.PutObject(request);
    }

    /// <summary>上传本地文件到 OSS 并返回签名 URL（供 ASR 使用 16kHz WAV 等）。</summary>
    public Task<string> UploadLocalFileAndGetSignedUrlAsync(
        string localFilePath,
        string extension,
        CancellationToken cancellationToken = default)
        => UploadLocalFileWithProgressAsync(localFilePath, extension, progress: null, cancellationToken);

    /// <summary>上传磁盘上的文件到 OSS 并返回签名 URL（可带进度，供视频先落盘再传 OSS）。</summary>
    public async Task<string> UploadLocalFileWithProgressAsync(
        string localFilePath,
        string extension,
        IProgress<UploadProgressReport>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            throw new InvalidOperationException("OSS 未启用或配置不完整。");
        }

        if (!File.Exists(localFilePath))
        {
            throw new FileNotFoundException("待上传文件不存在。", localFilePath);
        }

        string objectKey = BuildObjectKey(extension);
        var client = CreateClient();
        await Task.Run(
            () => PutLocalFileWithProgress(client, objectKey, localFilePath, progress),
            cancellationToken);

        progress?.Report(new UploadProgressReport(100, "上传完成"));

        var expireUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, _options.SignedUrlExpireSeconds));
        var signedUri = client.GeneratePresignedUri(_options.Bucket, objectKey, expireUtc);
        string url = signedUri.ToString();

        _logger.LogInformation(
            "OSS 上传本地文件成功：ObjectKey={ObjectKey}, Size={Size}",
            objectKey,
            new FileInfo(localFilePath).Length);

        return url;
    }

    private string BuildObjectKey(string extension)
    {
        string prefix = string.IsNullOrWhiteSpace(_options.Prefix)
            ? "talktrim/"
            : _options.Prefix;

        if (!prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        string id = Guid.NewGuid().ToString("N")[..16];
        return $"{prefix}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{id}{extension}";
    }

    /// <summary>
    /// 若 URL 指向本 Bucket 的对象，则重新生成签名 URL（供 DashScope ASR 拉取）。
    /// 无法识别时返回 null。
    /// </summary>
    public string? TryRefreshSignedUrl(string? url)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var objectKey = TryParseObjectKey(uri);
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            // 勿返回可能已过期的原签名 URL，否则 ASR 会长期停在 RUNNING
            if (uri.Host.Contains("aliyuncs.com", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "无法从 OSS 地址解析 objectKey，将重新抽取音频上传。Path={Path}",
                    uri.AbsolutePath);
            }

            return null;
        }

        var client = CreateClient();
        var expireUtc = DateTime.UtcNow.AddSeconds(Math.Max(60, _options.SignedUrlExpireSeconds));
        return client.GeneratePresignedUri(_options.Bucket, objectKey, expireUtc).ToString();
    }

    private string? TryParseObjectKey(Uri uri)
    {
        var path = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        // 虚拟主机: https://{bucket}.oss-xxx.aliyuncs.com/{key}
        if (uri.Host.StartsWith($"{_options.Bucket}.", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        // 路径风格: https://oss-xxx.aliyuncs.com/{bucket}/{key}
        if (path.StartsWith($"{_options.Bucket}/", StringComparison.OrdinalIgnoreCase))
        {
            return path[(_options.Bucket.Length + 1)..];
        }

        var prefix = string.IsNullOrWhiteSpace(_options.Prefix) ? "talktrim/" : _options.Prefix;
        if (!prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private OssClient CreateClient()
    {
        string raw = _options.Region.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("OSS Region 未配置。");
        }

        if (raw.Contains("aliyuncs.com", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            string endpoint = raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? raw : $"https://{raw}";
            return new OssClient(endpoint, _options.AccessKeyId, _options.AccessKeySecret);
        }

        string region = raw.StartsWith("oss-", StringComparison.OrdinalIgnoreCase) ? raw : $"oss-{raw}";
        return new OssClient($"https://{region}.aliyuncs.com", _options.AccessKeyId, _options.AccessKeySecret);
    }
}
