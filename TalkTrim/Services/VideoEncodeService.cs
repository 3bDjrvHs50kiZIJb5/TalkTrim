using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using TalkTrim.Entities.Video;
using TalkTrim.Models.Subtitle;
using NeoAdmin.Blazor.Extensions;

namespace TalkTrim.Services;

/// <summary>
/// 综合去口气、加速、双语字幕样式，用 ffmpeg 压制成片并上传。
/// </summary>
public sealed class VideoEncodeService
{
    private const string FinishedUploadDirectory = "video";

    /// <summary>封面图作为成片首帧时的时长（1 帧 @30fps）。</summary>
    private const double CoverFrameDurationSec = 1.0 / 30.0;

    private readonly OssUploadService _ossUploadService;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly ILogger<VideoEncodeService> _logger;

    public VideoEncodeService(
        OssUploadService ossUploadService,
        IWebHostEnvironment webHostEnvironment,
        ILogger<VideoEncodeService> logger)
    {
        _ossUploadService = ossUploadService;
        _webHostEnvironment = webHostEnvironment;
        _logger = logger;
    }

    public async Task<string> EncodeProjectAsync(
        Project project,
        string siteBaseUri,
        IProgress<ProjectJobProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = GetEncodeValidationErrors(project, siteBaseUri);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(' ', validationErrors));
        }

        if (string.IsNullOrWhiteSpace(project.VideoFileUrl))
        {
            throw new InvalidOperationException("请先填写视频素材 URL。");
        }

        ApplyPendingNumericFields(project);

        var inputPath = ResolveInputPath(project.VideoFileUrl, siteBaseUri);
        _logger.LogInformation(
            "开始视频压制：ProjectId={ProjectId}, ProjectName={ProjectName}, Speed={Speed}, BreathTrimSec={BreathTrimSec}, Input={InputPath}",
            project.Id,
            project.ProjectName,
            project.SpeedMultiplier,
            project.BreathTrimSeconds,
            inputPath);

        var workDir = Path.Combine(Path.GetTempPath(), $"talktrim-encode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);

        var assPath = Path.Combine(workDir, "subtitle.ass");
        var outputPath = Path.Combine(workDir, "finished.mp4");

        try
        {
            progress?.Report(new ProjectJobProgressReport("正在解析字幕…", 5));
            var cues = SubtitleSrtParser.MergeBilingual(
                project.EnglishSubtitles,
                project.ChineseSubtitles);

            var removals = BreathTrimService.BuildRemovalSegments(cues, project.BreathTrimSeconds);
            var shiftedCues = BreathTrimService.ShiftCues(cues, project.BreathTrimSeconds);
            var timedCues = AssSubtitleBuilder.ApplySpeedToCues(shiftedCues, project.SpeedMultiplier);
            _logger.LogDebug(
                "压制参数：ProjectId={ProjectId}, CueCount={CueCount}, RemovalSegments={RemovalCount}, HasAss={HasAss}",
                project.Id,
                timedCues.Count,
                removals.Count,
                timedCues.Count > 0);

            if (timedCues.Count > 0)
            {
                var opacity = project.SubtitleBackgroundOpacity;
                var english = AssSubtitleBuilder.BuildEnglishStyle(
                    CoalesceEnglish(project.EnglishSubtitleFontName, project.SubtitleFontName),
                    ResolveEnglishFontSize(project),
                    CoalesceEnglish(project.EnglishSubtitleFontColor, project.SubtitleFontColor),
                    CoalesceEnglish(project.EnglishSubtitleBackgroundColor, project.SubtitleBackgroundColor),
                    opacity);
                var chinese = AssSubtitleBuilder.BuildChineseStyle(
                    project.SubtitleFontName,
                    project.SubtitleFontSize,
                    project.SubtitleFontColor,
                    project.SubtitleBackgroundColor,
                    opacity);
                var ass = AssSubtitleBuilder.BuildBilingual(timedCues, english, chinese);
                await File.WriteAllTextAsync(assPath, ass, Encoding.UTF8, cancellationToken);
            }

            var coverImagePath = TryResolveCoverImagePath(
                project.ThumbnailUrl,
                siteBaseUri);

            progress?.Report(new ProjectJobProgressReport("正在压制视频（ffmpeg）…", 10));
            await EncodeVideoAsync(
                project.Id,
                inputPath,
                outputPath,
                removals,
                project.SpeedMultiplier,
                File.Exists(assPath) ? assPath : null,
                coverImagePath,
                progress,
                cancellationToken);

            var publishPath = outputPath;
            var outroPath = TryResolveOutroVideoPath(project.OutroVideoUrl, siteBaseUri);
            if (outroPath is not null)
            {
                var withOutroPath = Path.Combine(workDir, "finished-with-outro.mp4");
                progress?.Report(new ProjectJobProgressReport("正在拼接视频片尾…", 85));
                await AppendOutroVideoAsync(
                    project.Id,
                    outputPath,
                    outroPath,
                    withOutroPath,
                    progress,
                    cancellationToken);
                publishPath = withOutroPath;
                _logger.LogInformation(
                    "已拼接片尾：ProjectId={ProjectId}, OutroPath={OutroPath}",
                    project.Id,
                    outroPath);
            }

            progress?.Report(new ProjectJobProgressReport("正在上传成片…", 90));
            var finishedUrl = await PublishFinishedVideoAsync(publishPath, cancellationToken);
            _logger.LogInformation(
                "视频压制完成：ProjectId={ProjectId}, FinishedVideoUrl={FinishedVideoUrl}",
                project.Id,
                finishedUrl);
            return finishedUrl;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workDir))
                {
                    Directory.Delete(workDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理压制临时目录失败：{Dir}", workDir);
            }
        }
    }

    /// <summary>
    /// 压制前检查口播稿、字幕与缩略图是否齐备。
    /// </summary>
    public IReadOnlyList<string> GetEncodeValidationErrors(
        Project project,
        string siteBaseUri)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(project.ScriptContent))
        {
            errors.Add("请先填写口播稿。");
        }

        if (string.IsNullOrWhiteSpace(project.ChineseSubtitles)
            && string.IsNullOrWhiteSpace(project.EnglishSubtitles))
        {
            errors.Add("请先填写中英字幕。");
        }
        else
        {
            var cues = SubtitleSrtParser.MergeBilingual(
                project.EnglishSubtitles,
                project.ChineseSubtitles);
            if (cues.Count == 0)
            {
                errors.Add("字幕内容无效，请检查格式。");
            }
        }

        if (string.IsNullOrWhiteSpace(project.ThumbnailUrl))
        {
            errors.Add("请先上传或填写视频缩略图。");
        }
        else if (TryResolveCoverImagePath(project.ThumbnailUrl, siteBaseUri) is null)
        {
            errors.Add("缩略图文件不存在或地址无效，请重新上传。");
        }

        return errors;
    }

    private async Task EncodeVideoAsync(
        long projectId,
        string inputPath,
        string outputPath,
        IReadOnlyList<BreathTrimService.RemovalSegment> removals,
        decimal speedMultiplier,
        string? assPath,
        string? coverImagePath,
        IProgress<ProjectJobProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var durationSec = await FfmpegHelper.GetDurationSecondsAsync(inputPath, cancellationToken);
        var hasAudio = await FfmpegHelper.HasAudioStreamAsync(inputPath, cancellationToken);
        var prependCover = !string.IsNullOrWhiteSpace(coverImagePath);
        _logger.LogDebug(
            "ffmpeg 输入：ProjectId={ProjectId}, DurationSec={DurationSec}, HasAudio={HasAudio}, PrependCover={PrependCover}, RemovalCount={RemovalCount}",
            projectId,
            durationSec,
            hasAudio,
            prependCover,
            removals.Count);
        var videoFilter = BuildVideoFilterChain(
            removals,
            durationSec,
            speedMultiplier,
            assPath,
            prependCover);
        var audioFilter = BuildAudioFilterChain(
            removals,
            durationSec,
            speedMultiplier,
            prependCover,
            hasAudio);

        var args = new StringBuilder();
        args.Append("-y -nostdin ");
        if (prependCover)
        {
            args.Append(CultureInfo.InvariantCulture,
                $"-loop 1 -framerate 30 -t {CoverFrameDurationSec:F6} -i \"{coverImagePath}\" ");
        }

        args.Append(CultureInfo.InvariantCulture, $"-i \"{inputPath}\" ");
        args.Append("-filter_complex \"");
        args.Append(videoFilter);
        if (!string.IsNullOrWhiteSpace(audioFilter))
        {
            args.Append(';');
            args.Append(audioFilter);
        }

        args.Append("\" -map \"[outv]\" ");
        if (!string.IsNullOrWhiteSpace(audioFilter))
        {
            args.Append("-map \"[outa]\" ");
        }
        else
        {
            args.Append(hasAudio ? (prependCover ? "-map 1:a? " : "-map 0:a? ") : "-an ");
        }

        args.Append(
            "-c:v libx264 -preset veryfast -crf 20 -c:a aac -b:a 192k -movflags +faststart ");
        args.Append(CultureInfo.InvariantCulture, $"\"{outputPath}\"");

        var ffmpegArgs = args.ToString();
        _logger.LogDebug("ffmpeg 命令：ProjectId={ProjectId}, Arguments={Arguments}", projectId, ffmpegArgs);

        var encodeProgress = progress is null
            ? null
            : new Progress<ProjectJobProgressReport>(local =>
            {
                var overall = 10 + (int)Math.Round(local.Percent * 0.75);
                progress.Report(new ProjectJobProgressReport(
                    local.Message.StartsWith("ffmpeg", StringComparison.Ordinal)
                        ? local.Message
                        : $"正在压制视频… {overall}%",
                    overall));
            });

        try
        {
            await FfmpegHelper.RunAsync(
                ffmpegArgs,
                durationSec,
                encodeProgress,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ffmpeg 压制失败：ProjectId={ProjectId}", projectId);
            throw;
        }

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("压制完成但未生成输出文件。");
        }
    }

    /// <summary>
    /// 将片尾视频缩放到与主成片一致后，拼接到末尾（可选音轨，缺失侧补静音）。
    /// </summary>
    private async Task AppendOutroVideoAsync(
        long projectId,
        string mainPath,
        string outroPath,
        string outputPath,
        IProgress<ProjectJobProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var mainDuration = await FfmpegHelper.GetDurationSecondsAsync(mainPath, cancellationToken);
        var outroDuration = await FfmpegHelper.GetDurationSecondsAsync(outroPath, cancellationToken);
        var totalDuration = mainDuration + outroDuration;
        var mainHasAudio = await FfmpegHelper.HasAudioStreamAsync(mainPath, cancellationToken);
        var outroHasAudio = await FfmpegHelper.HasAudioStreamAsync(outroPath, cancellationToken);

        var filterParts = new List<string>
        {
            BuildScalePadFilter("1:v", "outrov"),
        };

        string mapVideo;
        string? mapAudio;

        filterParts.Add("[0:v][outrov]concat=n=2:v=1:a=0[outv]");
        mapVideo = "[outv]";

        if (mainHasAudio && outroHasAudio)
        {
            filterParts.Add("[0:a][1:a]concat=n=2:v=0:a=1[outa]");
            mapAudio = "[outa]";
        }
        else if (mainHasAudio)
        {
            filterParts.Add(FormattableString.Invariant(
                $"anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration={outroDuration:F3}[outrosilence]"));
            filterParts.Add("[0:a][outrosilence]concat=n=2:v=0:a=1[outa]");
            mapAudio = "[outa]";
        }
        else if (outroHasAudio)
        {
            filterParts.Add(FormattableString.Invariant(
                $"anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration={mainDuration:F3}[mainsilence]"));
            filterParts.Add("[mainsilence][1:a]concat=n=2:v=0:a=1[outa]");
            mapAudio = "[outa]";
        }
        else
        {
            mapAudio = null;
        }

        var args = new StringBuilder();
        args.Append("-y -nostdin ");
        args.Append(CultureInfo.InvariantCulture, $"-i \"{mainPath}\" ");
        args.Append(CultureInfo.InvariantCulture, $"-i \"{outroPath}\" ");
        args.Append("-filter_complex \"");
        args.Append(string.Join(';', filterParts));
        args.Append("\" ");
        args.Append(CultureInfo.InvariantCulture, $"-map \"{mapVideo}\" ");
        if (mapAudio is not null)
        {
            args.Append(CultureInfo.InvariantCulture, $"-map \"{mapAudio}\" ");
        }
        else
        {
            args.Append("-an ");
        }

        args.Append(
            "-c:v libx264 -preset veryfast -crf 20 -c:a aac -b:a 192k -movflags +faststart ");
        args.Append(CultureInfo.InvariantCulture, $"\"{outputPath}\"");

        var ffmpegArgs = args.ToString();
        _logger.LogDebug(
            "ffmpeg 拼接片尾：ProjectId={ProjectId}, MainHasAudio={MainHasAudio}, OutroHasAudio={OutroHasAudio}, Arguments={Arguments}",
            projectId,
            mainHasAudio,
            outroHasAudio,
            ffmpegArgs);

        var concatProgress = progress is null
            ? null
            : new Progress<ProjectJobProgressReport>(local =>
            {
                var overall = 85 + (int)Math.Round(local.Percent * 0.04);
                progress.Report(new ProjectJobProgressReport(
                    local.Message.StartsWith("ffmpeg", StringComparison.Ordinal)
                        ? local.Message
                        : $"正在拼接片尾… {overall}%",
                    overall));
            });

        await FfmpegHelper.RunAsync(
            ffmpegArgs,
            totalDuration,
            concatProgress,
            cancellationToken);

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("拼接片尾完成但未生成输出文件。");
        }
    }

    private static string BuildVideoFilterChain(
        IReadOnlyList<BreathTrimService.RemovalSegment> removals,
        double durationSec,
        decimal speedMultiplier,
        string? assPath,
        bool prependCoverFrame)
    {
        var parts = new List<string>();
        var sourceLabel = prependCoverFrame ? "1:v" : "0:v";
        var currentLabel = sourceLabel;

        if (removals.Count > 0)
        {
            var keep = BuildKeepIntervals(removals, durationSec);
            var trimParts = new List<string>();
            for (var i = 0; i < keep.Count; i++)
            {
                var (start, end) = keep[i];
                var label = $"v{i}";
                trimParts.Add(
                    FormattableString.Invariant(
                        $"[{sourceLabel}]trim=start={start:F3}:end={end:F3},setpts=PTS-STARTPTS[{label}]"));
                currentLabel = label;
            }

            if (keep.Count == 1)
            {
                parts.Add(trimParts[0].Replace("[v0]", "[outv0]", StringComparison.Ordinal));
                currentLabel = "outv0";
            }
            else
            {
                parts.AddRange(trimParts);
                var inputs = string.Join(string.Empty, Enumerable.Range(0, keep.Count).Select(i => $"[v{i}]"));
                parts.Add(FormattableString.Invariant(
                    $"{inputs}concat=n={keep.Count}:v=1:a=0[outv0]"));
                currentLabel = "outv0";
            }
        }

        if (speedMultiplier > 0 && speedMultiplier != 1m)
        {
            var speed = (double)speedMultiplier;
            parts.Add(FormattableString.Invariant(
                $"[{currentLabel}]setpts=PTS/{speed:F4}[outv1]"));
            currentLabel = "outv1";
        }

        parts.Add(BuildScalePadFilter(currentLabel, "outv2"));
        currentLabel = "outv2";

        if (!string.IsNullOrWhiteSpace(assPath))
        {
            var fontsDir = ResolveFontsDirectory();
            var escapedAss = FfmpegHelper.EscapeFilterPath(assPath);
            var escapedFonts = FfmpegHelper.EscapeFilterPath(fontsDir);
            parts.Add(FormattableString.Invariant(
                $"[{currentLabel}]ass='{escapedAss}':fontsdir='{escapedFonts}'[mainv]"));
        }
        else
        {
            parts.Add(FormattableString.Invariant($"[{currentLabel}]null[mainv]"));
        }

        if (prependCoverFrame)
        {
            parts.Add(BuildScalePadFilter("0:v", "coverv"));
            parts.Add("[coverv][mainv]concat=n=2:v=1:a=0[outv]");
        }
        else
        {
            parts.Add("[mainv]null[outv]");
        }

        return string.Join(';', parts);
    }

    private static string? BuildAudioFilterChain(
        IReadOnlyList<BreathTrimService.RemovalSegment> removals,
        double durationSec,
        decimal speedMultiplier,
        bool prependCoverFrame,
        bool hasAudioStream)
    {
        if (!hasAudioStream)
        {
            return null;
        }

        var parts = new List<string>();
        var sourceLabel = prependCoverFrame ? "1:a" : "0:a";
        var currentLabel = sourceLabel;

        if (removals.Count > 0)
        {
            var keep = BuildKeepIntervals(removals, durationSec);
            var trimParts = new List<string>();
            for (var i = 0; i < keep.Count; i++)
            {
                var (start, end) = keep[i];
                var label = $"a{i}";
                trimParts.Add(
                    FormattableString.Invariant(
                        $"[{sourceLabel}]atrim=start={start:F3}:end={end:F3},asetpts=PTS-STARTPTS[{label}]"));
                currentLabel = label;
            }

            if (keep.Count == 1)
            {
                parts.Add(trimParts[0].Replace("[a0]", "[outa0]", StringComparison.Ordinal));
                currentLabel = "outa0";
            }
            else
            {
                parts.AddRange(trimParts);
                var inputs = string.Join(string.Empty, Enumerable.Range(0, keep.Count).Select(i => $"[a{i}]"));
                parts.Add(FormattableString.Invariant(
                    $"{inputs}concat=n={keep.Count}:v=0:a=1[outa0]"));
                currentLabel = "outa0";
            }
        }

        if (speedMultiplier > 0 && speedMultiplier != 1m)
        {
            var speed = (double)speedMultiplier;
            // atempo 变速且保持音调，避免仅加速视频/错误 resample 产生「萝莉音」
            var atempo = string.Join(',', BuildAtempoFilters(speed));
            parts.Add(FormattableString.Invariant($"[{currentLabel}]{atempo}[outa1]"));
            currentLabel = "outa1";
        }

        if (parts.Count == 0 && !prependCoverFrame)
        {
            return null;
        }

        if (parts.Count == 0 || currentLabel == sourceLabel)
        {
            parts.Add(FormattableString.Invariant($"[{currentLabel}]anull[outa0]"));
            currentLabel = "outa0";
        }

        if (prependCoverFrame)
        {
            var delayMs = (int)Math.Round(CoverFrameDurationSec * 1000);
            parts.Add(FormattableString.Invariant($"[{currentLabel}]adelay={delayMs}|{delayMs}[outa]"));
        }
        else
        {
            parts.Add(FormattableString.Invariant($"[{currentLabel}]anull[outa]"));
        }

        return string.Join(';', parts);
    }

    private static string BuildScalePadFilter(string inputLabel, string outputLabel)
    {
        const string cond = "lte(iw\\,1920)*lte(ih\\,1080)";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[{inputLabel}]scale=w='if({cond}\\,1920\\,iw)':h='if({cond}\\,1080\\,ih)':force_original_aspect_ratio=decrease," +
            $"pad=w='if({cond}\\,1920\\,iw)':h='if({cond}\\,1080\\,ih)':x='(ow-iw)/2':y='(oh-ih)/2':color=black," +
            $"setsar=1[{outputLabel}]");
    }

    private static List<(double Start, double End)> BuildKeepIntervals(
        IReadOnlyList<BreathTrimService.RemovalSegment> removals,
        double durationSec)
    {
        var keeps = new List<(double Start, double End)>();
        var last = 0.0;
        foreach (var removal in removals)
        {
            var start = removal.StartMs / 1000.0;
            var end = removal.EndMs / 1000.0;
            if (start > last)
            {
                keeps.Add((last, start));
            }

            last = Math.Max(last, end);
        }

        if (last < durationSec)
        {
            keeps.Add((last, durationSec));
        }

        return keeps.Count > 0 ? keeps : [(0, durationSec)];
    }

    private static IEnumerable<string> BuildAtempoFilters(double speed)
    {
        var filters = new List<string>();
        var remaining = speed;
        while (remaining > 2.0)
        {
            filters.Add("atempo=2.0");
            remaining /= 2.0;
        }

        while (remaining < 0.5)
        {
            filters.Add("atempo=0.5");
            remaining /= 0.5;
        }

        filters.Add(FormattableString.Invariant($"atempo={remaining:F4}"));
        return filters;
    }

    private static string ResolveFontsDirectory()
    {
        var baseDir = AppContext.BaseDirectory;
        var fonts = Path.Combine(baseDir, "fonts");
        if (Directory.Exists(fonts))
        {
            return fonts;
        }

        throw new InvalidOperationException("未找到 fonts 目录，请确认项目已复制字幕字体到输出目录。");
    }

    private async Task<string> PublishFinishedVideoAsync(
        string localPath,
        CancellationToken cancellationToken)
    {
        if (_ossUploadService.IsEnabled)
        {
            return await _ossUploadService.UploadLocalFileAndGetSignedUrlAsync(
                localPath,
                ".mp4",
                cancellationToken);
        }

        var relativeDirectory = Path.Combine("uploads", FinishedUploadDirectory)
            .Replace('\\', '/');
        var fileName = $"finished-{Guid.NewGuid():N}.mp4";
        var physicalDirectory = Path.Combine(_webHostEnvironment.WebRootPath, relativeDirectory);
        Directory.CreateDirectory(physicalDirectory);
        var physicalPath = Path.Combine(physicalDirectory, fileName);
        File.Copy(localPath, physicalPath, overwrite: true);
        return "/" + Path.Combine(relativeDirectory, fileName).Replace('\\', '/');
    }

    private string ResolveInputPath(string videoFileUrl, string siteBaseUri)
    {
        var path = ResolveMediaPath(videoFileUrl, siteBaseUri, requireLocalFileExists: true);
        if (path is null)
        {
            throw new InvalidOperationException("无法解析视频素材地址。");
        }

        return path;
    }

    /// <summary>
    /// 若片尾 URL 可解析且文件有效，返回 ffmpeg 可读路径；否则为 null（未配置或无效时跳过拼接）。
    /// </summary>
    private string? TryResolveOutroVideoPath(string? outroVideoUrl, string siteBaseUri)
    {
        if (string.IsNullOrWhiteSpace(outroVideoUrl))
        {
            return null;
        }

        var path = ResolveMediaPath(outroVideoUrl, siteBaseUri, requireLocalFileExists: true);
        if (path is null)
        {
            _logger.LogWarning("片尾视频 URL 无法解析，跳过拼接：{Url}", outroVideoUrl);
            return null;
        }

        if (IsLocalMediaPath(path) && !File.Exists(path))
        {
            _logger.LogWarning("片尾视频文件不存在，跳过拼接：{Path}", path);
            return null;
        }

        return path;
    }

    /// <summary>
    /// 若缩略图 URL 可解析且本地文件存在（或远程地址可用），返回 ffmpeg 可读路径；否则为 null。
    /// </summary>
    private string? TryResolveCoverImagePath(string? thumbnailUrl, string siteBaseUri)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return null;
        }

        var path = ResolveMediaPath(thumbnailUrl, siteBaseUri, requireLocalFileExists: true);
        if (path is null)
        {
            _logger.LogInformation("缩略图 URL 无法解析，压制时不注入首帧。");
            return null;
        }

        if (IsLocalMediaPath(path))
        {
            if (!File.Exists(path))
            {
                _logger.LogInformation("缩略图文件不存在，压制时不注入首帧：{Path}", path);
                return null;
            }
        }

        return path;
    }

    private string? ResolveMediaPath(
        string mediaUrl,
        string siteBaseUri,
        bool requireLocalFileExists)
    {
        var refreshed = _ossUploadService.TryRefreshSignedUrl(mediaUrl);
        if (!string.IsNullOrWhiteSpace(refreshed))
        {
            return refreshed;
        }

        var absolute = MediaUrlHelper.ToAbsoluteMediaUrl(mediaUrl, siteBaseUri);
        if (string.IsNullOrWhiteSpace(absolute))
        {
            return null;
        }

        if (!IsLocalMediaUrl(absolute))
        {
            return absolute;
        }

        var relative = mediaUrl.Trim();
        if (relative.StartsWith('/'))
        {
            relative = relative[1..];
        }

        var physical = Path.Combine(_webHostEnvironment.WebRootPath, relative);
        if (requireLocalFileExists && !File.Exists(physical))
        {
            return null;
        }

        return physical;
    }

    private static bool IsLocalMediaUrl(string absolute) =>
        absolute.Contains("localhost", StringComparison.OrdinalIgnoreCase)
        || absolute.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalMediaPath(string path) =>
        !path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        && !path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string? CoalesceEnglish(string? english, string? chinese) =>
        string.IsNullOrWhiteSpace(english) ? chinese : english;

    private static int ResolveEnglishFontSize(Project project)
    {
        if (project.EnglishSubtitleFontSize > 0)
        {
            return project.EnglishSubtitleFontSize;
        }

        return project.SubtitleFontSize > 0 ? project.SubtitleFontSize - 12 : 32;
    }

    private static void ApplyPendingNumericFields(Project project)
    {
        if (project.SpeedMultiplier <= 0)
        {
            project.SpeedMultiplier = 1.25m;
        }

        if (project.BreathTrimSeconds < 0)
        {
            project.BreathTrimSeconds = 0;
        }
    }
}
