using TalkTrim.Entities.Video;

namespace TalkTrim.Services;

/// <summary>
/// 执行单条视频项目后台任务（在独立 DI Scope 中调用）。
/// </summary>
public sealed class ProjectJobExecutor
{
    private readonly ProjectJobService _jobService;
    private readonly VideoTranscriptionService _transcriptionService;
    private readonly VideoEncodeService _encodeService;
    private readonly ILogger<ProjectJobExecutor> _logger;

    public ProjectJobExecutor(
        ProjectJobService jobService,
        VideoTranscriptionService transcriptionService,
        VideoEncodeService encodeService,
        ILogger<ProjectJobExecutor> logger)
    {
        _jobService = jobService;
        _transcriptionService = transcriptionService;
        _encodeService = encodeService;
        _logger = logger;
    }

    public async Task ExecuteAsync(long jobId, CancellationToken cancellationToken = default)
    {
        var job = await _jobService.GetJobAsync(jobId, cancellationToken);
        if (job is null)
        {
            _logger.LogWarning("后台任务不存在：JobId={JobId}", jobId);
            return;
        }

        if (job.Status is ProjectJobStatus.Succeeded or ProjectJobStatus.Failed)
        {
            return;
        }

        await _jobService.MarkRunningAsync(jobId, cancellationToken);
        _logger.LogInformation(
            "开始处理后台任务：JobId={JobId}, ProjectId={ProjectId}, JobType={JobType}",
            jobId,
            job.ProjectId,
            job.JobType);

        try
        {
            if (job.JobType == ProjectJobType.Transcribe)
            {
                await ExecuteTranscribeAsync(job, cancellationToken);
            }
            else if (job.JobType == ProjectJobType.Encode)
            {
                await ExecuteEncodeAsync(job, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"未知任务类型：{job.JobType}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "后台任务失败：JobId={JobId}, ProjectId={ProjectId}", jobId, job.ProjectId);
            await _jobService.MarkFailedAsync(jobId, ex.Message, cancellationToken);
        }
    }

    private async Task ExecuteTranscribeAsync(ProjectJob job, CancellationToken cancellationToken)
    {
        var project = await _jobService.GetProjectAsync(job.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException("项目不存在。");

        var progress = CreateProgressLogger(job);

        var result = await _transcriptionService.TranscribeAsync(
            project.VideoFileUrl ?? string.Empty,
            string.IsNullOrWhiteSpace(project.WavUrl) ? null : project.WavUrl,
            job.SiteBaseUri,
            progress,
            cancellationToken);

        project.WavUrl = result.WavUrl;
        project.ScriptContent = result.ScriptContent;
        project.EnglishSubtitles = result.EnglishSubtitles;
        project.ChineseSubtitles = result.ChineseSubtitles;

        await _jobService.SaveProjectSnapshotAsync(project, cancellationToken);

        var lineCount = result.ScriptContent.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        _logger.LogInformation(
            "口播稿识别完成：JobId={JobId}, ProjectId={ProjectId}, ScriptLines={LineCount}",
            job.Id,
            job.ProjectId,
            lineCount);
        await _jobService.MarkSucceededAsync(
            job.Id,
            $"识别完成，已自动保存（{lineCount} 句）",
            cancellationToken);
    }

    private async Task ExecuteEncodeAsync(ProjectJob job, CancellationToken cancellationToken)
    {
        var project = await _jobService.GetProjectAsync(job.ProjectId, cancellationToken)
            ?? throw new InvalidOperationException("项目不存在。");

        var validationErrors = _encodeService.GetEncodeValidationErrors(project, job.SiteBaseUri);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(' ', validationErrors));
        }

        var progress = CreateProgressLogger(job);

        var finishedUrl = await _encodeService.EncodeProjectAsync(
            project,
            job.SiteBaseUri,
            progress,
            cancellationToken);

        project.FinishedVideoUrl = finishedUrl;
        await _jobService.SaveProjectSnapshotAsync(project, cancellationToken);
        _logger.LogInformation(
            "视频压制完成：JobId={JobId}, ProjectId={ProjectId}, FinishedVideoUrl={FinishedVideoUrl}",
            job.Id,
            job.ProjectId,
            finishedUrl);
        await _jobService.MarkSucceededAsync(job.Id, "压制完成，成片地址已自动保存。", cancellationToken);
    }

    private IProgress<ProjectJobProgressReport> CreateProgressLogger(ProjectJob job)
    {
        var lastPercent = -1;
        var lastMessage = string.Empty;
        var lastDbWrite = DateTime.MinValue;

        return new Progress<ProjectJobProgressReport>(report =>
        {
            var percent = Math.Clamp(report.Percent, 0, 100);
            var now = DateTime.UtcNow;
            var shouldWrite = report.Message != lastMessage
                || percent - lastPercent >= 3
                || percent >= 99
                || (now - lastDbWrite).TotalSeconds >= 2;

            if (!shouldWrite)
            {
                return;
            }

            lastPercent = percent;
            lastMessage = report.Message;
            lastDbWrite = now;
            _logger.LogDebug(
                "任务进度 JobId={JobId}, ProjectId={ProjectId}, Percent={Percent}: {Message}",
                job.Id,
                job.ProjectId,
                percent,
                report.Message);
            _ = _jobService.UpdateProgressAsync(
                job.Id,
                report.Message,
                percent,
                cancellationToken: CancellationToken.None);
        });
    }
}
