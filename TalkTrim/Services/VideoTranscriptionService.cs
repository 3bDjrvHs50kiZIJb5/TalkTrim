using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using TalkTrim.Services.DashScope;

namespace TalkTrim.Services;

public sealed class VideoTranscriptionResult
{
    public string WavUrl { get; init; } = string.Empty;
    public string ScriptContent { get; init; } = string.Empty;
    public string EnglishSubtitles { get; init; } = string.Empty;
    public string ChineseSubtitles { get; init; } = string.Empty;
}

/// <summary>
/// 从视频 URL 识别口播稿并生成中英双语字幕（SRT）。
/// </summary>
public sealed class VideoTranscriptionService
{
    private readonly ParaformerAsrService _asrService;
    private readonly SubtitleTranslationService _translationService;
    private readonly OssUploadService _ossUploadService;
    private readonly VideoAsrAudioPrepService _audioPrepService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly DashScopeOptions _dashScopeOptions;
    private readonly ILogger<VideoTranscriptionService> _logger;

    public VideoTranscriptionService(
        ParaformerAsrService asrService,
        SubtitleTranslationService translationService,
        OssUploadService ossUploadService,
        VideoAsrAudioPrepService audioPrepService,
        IWebHostEnvironment webHostEnvironment,
        IOptions<DashScopeOptions> dashScopeOptions,
        ILogger<VideoTranscriptionService> logger)
    {
        _asrService = asrService;
        _translationService = translationService;
        _ossUploadService = ossUploadService;
        _audioPrepService = audioPrepService;
        _webHostEnvironment = webHostEnvironment;
        _dashScopeOptions = dashScopeOptions.Value;
        _logger = logger;
    }

    /// <param name="existingWavUrl">若已填写 WAV 音频 URL，则跳过从视频提取音频，直接用于识别。</param>
    /// <param name="videoFileUrl">视频素材 URL；无已有 WAV 时用于提取音频。</param>
    /// <param name="videoFileLocalUrl">本站本地视频路径；有则优先用于 ffmpeg 提取 WAV，避免从 OSS 下载。</param>
    /// <param name="siteBaseUri">站点根地址，用于解析相对媒体路径。</param>
    /// <param name="progress">任务进度回调。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<VideoTranscriptionResult> TranscribeAsync(
        string videoFileUrl,
        string? videoFileLocalUrl,
        string? existingWavUrl,
        string siteBaseUri,
        IProgress<ProjectJobProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "开始口播稿识别：HasExistingWav={HasExistingWav}, VideoFileUrl={VideoFileUrl}",
            !string.IsNullOrWhiteSpace(existingWavUrl),
            videoFileUrl);

        progress?.Report(new ProjectJobProgressReport("准备识别口播稿…", 5));

        string wavUrl;
        if (!string.IsNullOrWhiteSpace(existingWavUrl))
        {
            progress?.Report(new ProjectJobProgressReport("使用已有 WAV 音频进行识别…", 15));
            wavUrl = ResolvePublicMediaUrl(existingWavUrl, siteBaseUri);
            _logger.LogDebug("使用已有 WAV：{WavUrl}", wavUrl);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(videoFileUrl))
            {
                throw new InvalidOperationException("请先填写或上传视频素材 URL。");
            }

            var extractInput = ResolveVideoInputForAudioExtract(
                videoFileUrl,
                videoFileLocalUrl,
                siteBaseUri);
            progress?.Report(new ProjectJobProgressReport("正在从视频提取音频（16kHz WAV）…", 10));

            var extractProgress = progress is null
                ? null
                : new Progress<ProjectJobProgressReport>(local =>
                {
                    progress.Report(new ProjectJobProgressReport(
                        local.Message,
                        Math.Clamp(local.Percent, 5, 20)));
                });

            wavUrl = await _audioPrepService.PrepareAsrAudioUrlAsync(
                extractInput,
                extractProgress,
                cancellationToken);
        }

        progress?.Report(new ProjectJobProgressReport("正在提交语音识别…", 28));
        var asrCues = await _asrService.TranscribeAsync(wavUrl, progress, cancellationToken);
        if (asrCues.Count == 0)
        {
            throw new InvalidOperationException("未识别到任何语音内容。");
        }

        _logger.LogInformation("ASR 识别完成：CueCount={CueCount}", asrCues.Count);

        if (_dashScopeOptions.IsChineseAsrPrimary)
        {
            progress?.Report(new ProjectJobProgressReport("正在翻译为英文字幕…", 72));
            var withEnglish = await _translationService.TranslateCuesAsync(
                asrCues,
                progress,
                cancellationToken);
            _logger.LogInformation("口播稿识别流程结束（中文主语言）：CueCount={CueCount}", withEnglish.Count);
            progress?.Report(new ProjectJobProgressReport("识别与翻译完成，正在保存…", 98));
            return new VideoTranscriptionResult
            {
                WavUrl = wavUrl,
                ScriptContent = SubtitleSrtFormatter.JoinScriptLines(asrCues),
                ChineseSubtitles = SubtitleSrtFormatter.ToSrt(asrCues, chineseLines: false),
                EnglishSubtitles = SubtitleSrtFormatter.ToSrt(withEnglish, chineseLines: true),
            };
        }

        progress?.Report(new ProjectJobProgressReport("正在翻译为中文字幕…", 72));
        var withChinese = await _translationService.TranslateCuesAsync(
            asrCues,
            progress,
            cancellationToken);
        _logger.LogInformation("口播稿识别流程结束（英文主语言）：CueCount={CueCount}", withChinese.Count);
        progress?.Report(new ProjectJobProgressReport("识别与翻译完成，正在保存…", 98));
        return new VideoTranscriptionResult
        {
            WavUrl = wavUrl,
            ScriptContent = SubtitleSrtFormatter.JoinScriptLines(asrCues),
            EnglishSubtitles = SubtitleSrtFormatter.ToSrt(asrCues, chineseLines: false),
            ChineseSubtitles = SubtitleSrtFormatter.ToSrt(withChinese, chineseLines: true),
        };
    }

    private string ResolveVideoInputForAudioExtract(
        string videoFileUrl,
        string? videoFileLocalUrl,
        string siteBaseUri)
    {
        if (!string.IsNullOrWhiteSpace(videoFileLocalUrl)
            && MediaUrlHelper.TryResolveWebRootPhysicalPath(
                videoFileLocalUrl,
                _webHostEnvironment.WebRootPath,
                out var localPhysical))
        {
            _logger.LogInformation("口播稿识别使用本地视频文件：{Path}", localPhysical);
            return localPhysical;
        }

        return ResolvePublicMediaUrl(videoFileUrl, siteBaseUri);
    }

    private string ResolvePublicMediaUrl(string videoFileUrl, string siteBaseUri)
    {
        if (string.IsNullOrWhiteSpace(videoFileUrl))
        {
            throw new InvalidOperationException("视频 URL 为空。");
        }

        var refreshed = _ossUploadService.TryRefreshSignedUrl(videoFileUrl);
        if (!string.IsNullOrWhiteSpace(refreshed))
        {
            return refreshed;
        }

        var absolute = MediaUrlHelper.ToAbsoluteMediaUrl(videoFileUrl, siteBaseUri)
            ?? throw new InvalidOperationException("无法解析视频地址。");

        if (absolute.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || absolute.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "当前视频为本站本地地址，阿里云 ASR 无法访问。请开启 OSS 上传，或改用公网可访问的视频 URL。");
        }

        _logger.LogInformation("ASR 使用视频地址: {Url}", absolute);
        return absolute;
    }
}
