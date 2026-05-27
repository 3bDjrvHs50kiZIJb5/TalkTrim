namespace TalkTrim.Services;

/// <summary>
/// ASR 前将视频转为 16kHz 单声道 WAV 并上传 OSS。
/// 远程视频先一次性下载到本地临时文件，再 ffmpeg 提取（避免边下边转导致过慢）。
/// </summary>
public sealed class VideoAsrAudioPrepService
{
    private readonly OssUploadService _ossUploadService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<VideoAsrAudioPrepService> _logger;

    public VideoAsrAudioPrepService(
        OssUploadService ossUploadService,
        HttpClient httpClient,
        ILogger<VideoAsrAudioPrepService> logger)
    {
        _ossUploadService = ossUploadService;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> PrepareAsrAudioUrlAsync(
        string videoUrl,
        IProgress<ProjectJobProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_ossUploadService.IsEnabled)
        {
            throw new InvalidOperationException("语音识别需要 OSS：请先在设置中启用 OSS 并上传视频。");
        }

        var tempWav = Path.Combine(Path.GetTempPath(), $"talktrim-asr-{Guid.NewGuid():N}.wav");
        try
        {
            _logger.LogDebug("从视频提取 ASR 音频：VideoUrl={VideoUrl}", videoUrl);
            await ExtractAudioToWavAsync(videoUrl, tempWav, progress, cancellationToken);

            progress?.Report(new ProjectJobProgressReport("正在上传音频到 OSS…", 20));
            var signedUrl = await _ossUploadService.UploadLocalFileAndGetSignedUrlAsync(
                tempWav,
                ".wav",
                cancellationToken);

            _logger.LogInformation("ASR 音频已准备：WAV 大小 {Size} 字节", new FileInfo(tempWav).Length);
            return signedUrl;
        }
        finally
        {
            if (File.Exists(tempWav))
            {
                File.Delete(tempWav);
            }
        }
    }

    private async Task ExtractAudioToWavAsync(
        string videoUrl,
        string outputWavPath,
        IProgress<ProjectJobProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        string? tempVideoPath = null;
        try
        {
            var inputPath = await ResolveLocalInputPathAsync(videoUrl, progress, cancellationToken);
            if (!string.Equals(inputPath, videoUrl, StringComparison.Ordinal))
            {
                tempVideoPath = inputPath;
            }

            double? durationSec = null;
            try
            {
                durationSec = await FfmpegHelper.GetDurationSecondsAsync(inputPath, cancellationToken);
            }
            catch
            {
                // 部分格式可能无法探测时长，仍继续提取
            }

            progress?.Report(new ProjectJobProgressReport("正在从本地视频提取音频…", 12));
            var args =
                $"-y -hide_banner -loglevel error -threads 0 -i \"{inputPath}\" -vn -map 0:a:0 -sn -dn " +
                $"-ac 1 -ar 16000 -c:a pcm_s16le -f wav \"{outputWavPath}\"";
            var ffmpegProgress = MapFfmpegProgress(progress);
            await FfmpegHelper.RunAsync(args, durationSec, ffmpegProgress, cancellationToken);

            if (!File.Exists(outputWavPath))
            {
                throw new InvalidOperationException("ffmpeg 提取音频完成但未生成 WAV 文件。");
            }
        }
        finally
        {
            if (tempVideoPath is not null && File.Exists(tempVideoPath))
            {
                File.Delete(tempVideoPath);
            }
        }
    }

    private async Task<string> ResolveLocalInputPathAsync(
        string videoUrl,
        IProgress<ProjectJobProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        if (IsRemoteMediaUrl(videoUrl))
        {
            return await DownloadToTempFileAsync(videoUrl, progress, cancellationToken);
        }

        if (!File.Exists(videoUrl))
        {
            throw new InvalidOperationException($"视频文件不存在：{videoUrl}");
        }

        _logger.LogDebug("使用本地视频路径：{Path}", videoUrl);
        return videoUrl;
    }

    private async Task<string> DownloadToTempFileAsync(
        string url,
        IProgress<ProjectJobProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ProjectJobProgressReport("正在下载视频到本地…", 5));

        var extension = GuessExtensionFromUrl(url);
        var tempPath = Path.Combine(Path.GetTempPath(), $"talktrim-video-{Guid.NewGuid():N}{extension}");

        _logger.LogInformation("开始下载视频：Url={Url}, TempPath={TempPath}", url, tempPath);

        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"下载视频失败（HTTP {(int)response.StatusCode}）：{url}");
        }

        var totalBytes = response.Content.Headers.ContentLength;
        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(tempPath);

        if (totalBytes is > 0)
        {
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await networkStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                var downloadPercent = (int)Math.Clamp(downloaded * 100 / totalBytes.Value, 0, 100);
                var overall = 5 + downloadPercent * 6 / 100;
                progress?.Report(new ProjectJobProgressReport(
                    $"正在下载视频… {downloadPercent}%",
                    overall));
            }
        }
        else
        {
            await networkStream.CopyToAsync(fileStream, cancellationToken);
        }

        var size = new FileInfo(tempPath).Length;
        if (size <= 0)
        {
            File.Delete(tempPath);
            throw new InvalidOperationException("下载的视频文件为空。");
        }

        _logger.LogInformation("视频已下载到本地：Size={Size} 字节, Path={Path}", size, tempPath);
        return tempPath;
    }

    private static bool IsRemoteMediaUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static IProgress<ProjectJobProgressReport>? MapFfmpegProgress(
        IProgress<ProjectJobProgressReport>? progress)
    {
        if (progress is null)
        {
            return null;
        }

        return new Progress<ProjectJobProgressReport>(local =>
        {
            var overall = 12 + local.Percent * 7 / 100;
            progress.Report(new ProjectJobProgressReport(local.Message, overall));
        });
    }

    private static string GuessExtensionFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return ".mp4";
        }

        var path = uri.AbsolutePath;
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
        {
            return ".mp4";
        }

        return extension.ToLowerInvariant();
    }
}
