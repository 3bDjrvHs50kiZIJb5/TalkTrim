using FreeSql;
using TalkTrim.Entities.Video;

namespace TalkTrim.Services;

/// <summary>
/// 视频项目后台任务的入队、查询与项目快照保存。
/// </summary>
public sealed class ProjectJobService
{
    private static readonly ProjectJobStatus[] ActiveStatuses =
    [
        ProjectJobStatus.Pending,
        ProjectJobStatus.Running,
    ];

    private readonly IFreeSql _freeSql;
    private readonly ProjectJobQueue _queue;
    private readonly ProjectJobCancellationRegistry _cancellationRegistry;
    private readonly ILogger<ProjectJobService> _logger;

    public ProjectJobService(
        IFreeSql freeSql,
        ProjectJobQueue queue,
        ProjectJobCancellationRegistry cancellationRegistry,
        ILogger<ProjectJobService> logger)
    {
        _freeSql = freeSql;
        _queue = queue;
        _cancellationRegistry = cancellationRegistry;
        _logger = logger;
    }

    public async Task<long> EnqueueTranscribeAsync(
        Project project,
        string siteBaseUri,
        CancellationToken cancellationToken = default)
    {
        EnsureProjectSaved(project);
        await SaveProjectSnapshotAsync(project, cancellationToken);
        await EnsureNoActiveJobAsync(project.Id, cancellationToken);

        var job = new ProjectJob
        {
            ProjectId = project.Id,
            JobType = ProjectJobType.Transcribe,
            Status = ProjectJobStatus.Pending,
            ProgressMessage = "排队中，等待识别口播稿…",
            SiteBaseUri = siteBaseUri.TrimEnd('/'),
        };

        job.Id = await _freeSql.Insert(job).ExecuteIdentityAsync(cancellationToken);
        await _queue.EnqueueAsync(job.Id, cancellationToken);
        _logger.LogInformation(
            "已入队口播稿识别：JobId={JobId}, ProjectId={ProjectId}, ProjectName={ProjectName}",
            job.Id,
            project.Id,
            project.ProjectName);
        return job.Id;
    }

    public async Task<long> EnqueueEncodeAsync(
        Project project,
        string siteBaseUri,
        CancellationToken cancellationToken = default)
    {
        EnsureProjectSaved(project);
        await SaveProjectSnapshotAsync(project, cancellationToken);
        await EnsureNoActiveJobAsync(project.Id, cancellationToken);

        var job = new ProjectJob
        {
            ProjectId = project.Id,
            JobType = ProjectJobType.Encode,
            Status = ProjectJobStatus.Pending,
            ProgressMessage = "排队中，等待视频压制…",
            SiteBaseUri = siteBaseUri.TrimEnd('/'),
        };

        job.Id = await _freeSql.Insert(job).ExecuteIdentityAsync(cancellationToken);
        await _queue.EnqueueAsync(job.Id, cancellationToken);
        _logger.LogInformation(
            "已入队视频压制：JobId={JobId}, ProjectId={ProjectId}, ProjectName={ProjectName}",
            job.Id,
            project.Id,
            project.ProjectName);
        return job.Id;
    }

    public Task<List<ProjectJob>> GetActiveJobsAsync(long projectId, CancellationToken cancellationToken = default) =>
        _freeSql.Select<ProjectJob>()
            .Where(j => j.ProjectId == projectId && ActiveStatuses.Contains(j.Status))
            .OrderByDescending(j => j.Id)
            .ToListAsync(cancellationToken);

    public async Task<List<ActiveProjectJobRow>> ListAllActiveJobsAsync(
        long? userId = null,
        CancellationToken cancellationToken = default)
    {
        var jobs = await _freeSql.Select<ProjectJob>()
            .Where(j => ActiveStatuses.Contains(j.Status))
            .OrderByDescending(j => j.Id)
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            return [];
        }

        var projectIds = jobs.Select(j => j.ProjectId).Distinct().ToList();
        var projects = await _freeSql.Select<Project>()
            .Where(p => projectIds.Contains(p.Id))
            .WhereIf(userId.HasValue, p => p.UserId == userId!.Value)
            .ToListAsync(cancellationToken);
        var projectMap = projects.ToDictionary(p => p.Id);

        return jobs
            .Where(job => projectMap.ContainsKey(job.ProjectId))
            .Select(job =>
            {
                var project = projectMap[job.ProjectId];
                return new ActiveProjectJobRow
                {
                    Job = job,
                    ProjectName = project.ProjectName,
                    ProjectCode = project.ProjectCode,
                };
            })
            .ToList();
    }

    public async Task<int> CountAllActiveJobsAsync(
        long? userId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _freeSql.Select<ProjectJob>()
            .Where(j => ActiveStatuses.Contains(j.Status));

        if (userId.HasValue)
        {
            query = query.Where(j => _freeSql.Select<Project>()
                .Where(p => p.Id == j.ProjectId && p.UserId == userId.Value)
                .Any());
        }

        var count = await query.CountAsync(cancellationToken);
        return (int)count;
    }

    public async Task<ProjectJob?> GetJobAsync(long jobId, CancellationToken cancellationToken = default) =>
        await _freeSql.Select<ProjectJob>()
            .Where(j => j.Id == jobId)
            .ToOneAsync(cancellationToken);

    public async Task<ProjectJob?> GetLatestJobAsync(
        long projectId,
        ProjectJobType jobType,
        CancellationToken cancellationToken = default) =>
        await _freeSql.Select<ProjectJob>()
            .Where(j => j.ProjectId == projectId && j.JobType == jobType)
            .OrderByDescending(j => j.Id)
            .ToOneAsync(cancellationToken);

    public async Task<Project?> GetProjectAsync(long projectId, CancellationToken cancellationToken = default) =>
        await _freeSql.Select<Project>()
            .Where(p => p.Id == projectId)
            .ToOneAsync(cancellationToken);

    public async Task UpdateProgressAsync(
        long jobId,
        string message,
        int? percent = null,
        CancellationToken cancellationToken = default)
    {
        var updater = _freeSql.Update<ProjectJob>()
            .Where(j => j.Id == jobId)
            .Set(j => j.ProgressMessage, message);

        if (percent.HasValue)
        {
            updater = updater.Set(j => j.ProgressPercent, percent.Value);
        }

        await updater.ExecuteAffrowsAsync(cancellationToken);
    }

    public async Task MarkRunningAsync(long jobId, CancellationToken cancellationToken = default)
    {
        await _freeSql.Update<ProjectJob>()
            .Where(j => j.Id == jobId)
            .Set(j => j.Status, ProjectJobStatus.Running)
            .Set(j => j.StartedAt, DateTime.Now)
            .Set(j => j.ProgressPercent, 0)
            .Set(j => j.ProgressMessage, "任务已开始…")
            .ExecuteAffrowsAsync(cancellationToken);
        _logger.LogInformation("后台任务开始执行：JobId={JobId}", jobId);
    }

    public async Task MarkSucceededAsync(long jobId, string message, CancellationToken cancellationToken = default)
    {
        await _freeSql.Update<ProjectJob>()
            .Where(j => j.Id == jobId)
            .Set(j => j.Status, ProjectJobStatus.Succeeded)
            .Set(j => j.ProgressMessage, message)
            .Set(j => j.ProgressPercent, 100)
            .Set(j => j.FinishedAt, DateTime.Now)
            .Set(j => j.ErrorMessage, string.Empty)
            .ExecuteAffrowsAsync(cancellationToken);
        _logger.LogInformation("后台任务成功：JobId={JobId}, Message={Message}", jobId, message);
    }

    public async Task MarkFailedAsync(long jobId, string error, CancellationToken cancellationToken = default)
    {
        await _freeSql.Update<ProjectJob>()
            .Where(j => j.Id == jobId)
            .Set(j => j.Status, ProjectJobStatus.Failed)
            .Set(j => j.ErrorMessage, error.Length > 2000 ? error[..2000] : error)
            .Set(j => j.ProgressMessage, "任务失败")
            .Set(j => j.FinishedAt, DateTime.Now)
            .ExecuteAffrowsAsync(cancellationToken);
        _logger.LogWarning("后台任务失败：JobId={JobId}, Error={Error}", jobId, error);
    }

    public async Task MarkCancelledAsync(
        long jobId,
        string message = "用户已取消任务",
        CancellationToken cancellationToken = default)
    {
        await _freeSql.Update<ProjectJob>()
            .Where(j => j.Id == jobId)
            .Set(j => j.Status, ProjectJobStatus.Cancelled)
            .Set(j => j.ProgressMessage, message)
            .Set(j => j.ErrorMessage, string.Empty)
            .Set(j => j.FinishedAt, DateTime.Now)
            .ExecuteAffrowsAsync(cancellationToken);
        _logger.LogInformation("后台任务已取消：JobId={JobId}, Message={Message}", jobId, message);
    }

    /// <summary>取消排队中或运行中的任务；非管理员仅能取消自己的项目任务。</summary>
    public async Task<bool> CancelJobAsync(
        long jobId,
        long? userId = null,
        CancellationToken cancellationToken = default)
    {
        var job = await GetJobAsync(jobId, cancellationToken);
        if (job is null || !ActiveStatuses.Contains(job.Status))
        {
            return false;
        }

        if (userId.HasValue)
        {
            var project = await GetProjectAsync(job.ProjectId, cancellationToken);
            if (project is null || project.UserId != userId.Value)
            {
                return false;
            }
        }

        if (job.Status == ProjectJobStatus.Pending)
        {
            await MarkCancelledAsync(jobId, "用户已取消任务", cancellationToken);
            return true;
        }

        await UpdateProgressAsync(jobId, "正在取消…", cancellationToken: cancellationToken);
        _cancellationRegistry.TryCancel(jobId);
        _logger.LogInformation("已请求取消运行中的任务：JobId={JobId}", jobId);
        return true;
    }

    public Task SaveProjectSnapshotAsync(Project project, CancellationToken cancellationToken = default) =>
        _freeSql.Update<Project>().SetSource(project).ExecuteAffrowsAsync(cancellationToken);

    public static void CopyProjectContent(Project source, Project target)
    {
        target.ProjectName = source.ProjectName;
        target.ProjectCode = source.ProjectCode;
        target.Remark = source.Remark;
        target.Description = source.Description;
        target.ThumbnailUrl = source.ThumbnailUrl;
        target.OutroVideoUrl = source.OutroVideoUrl;
        target.Keywords = source.Keywords;
        target.VideoFileUrl = source.VideoFileUrl;
        target.WavUrl = source.WavUrl;
        target.ScriptContent = source.ScriptContent;
        target.ChineseSubtitles = source.ChineseSubtitles;
        target.EnglishSubtitles = source.EnglishSubtitles;
        target.SubtitleFontName = source.SubtitleFontName;
        target.SubtitleFontSize = source.SubtitleFontSize;
        target.SubtitleFontColor = source.SubtitleFontColor;
        target.SubtitleBackgroundColor = source.SubtitleBackgroundColor;
        target.EnglishSubtitleFontName = source.EnglishSubtitleFontName;
        target.EnglishSubtitleFontSize = source.EnglishSubtitleFontSize;
        target.EnglishSubtitleFontColor = source.EnglishSubtitleFontColor;
        target.EnglishSubtitleBackgroundColor = source.EnglishSubtitleBackgroundColor;
        target.SubtitleBackgroundOpacity = source.SubtitleBackgroundOpacity;
        target.SpeedMultiplier = source.SpeedMultiplier;
        target.BreathTrimSeconds = source.BreathTrimSeconds;
        target.FinishedVideoUrl = source.FinishedVideoUrl;
        target.UserId = source.UserId;
        target.Status = source.Status;
        target.ModifiedTime = source.ModifiedTime;
    }

    /// <summary>
    /// 仅同步后台任务写入数据库的字段，避免轮询覆盖表单中尚未保存的编辑（如缩略图上传）。
    /// </summary>
    public static void ApplyJobResultsFromDatabase(Project source, Project target)
    {
        target.WavUrl = source.WavUrl;
        target.ScriptContent = source.ScriptContent;
        target.ChineseSubtitles = source.ChineseSubtitles;
        target.EnglishSubtitles = source.EnglishSubtitles;
        target.FinishedVideoUrl = source.FinishedVideoUrl;
        target.ModifiedTime = source.ModifiedTime;
    }

    private static void EnsureProjectSaved(Project project)
    {
        if (project.Id <= 0)
        {
            throw new InvalidOperationException("请先保存项目后再提交后台任务。");
        }
    }

    private async Task EnsureNoActiveJobAsync(long projectId, CancellationToken cancellationToken)
    {
        var active = await GetActiveJobsAsync(projectId, cancellationToken);
        if (active.Count > 0)
        {
            var running = active[0];
            var typeLabel = running.JobType == ProjectJobType.Transcribe ? "口播稿识别" : "视频压制";
            throw new InvalidOperationException($"该项目已有进行中的{typeLabel}任务，请稍候完成后再试。");
        }
    }
}
