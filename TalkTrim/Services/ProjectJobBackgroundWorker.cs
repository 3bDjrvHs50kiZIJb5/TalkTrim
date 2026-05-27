using FreeSql;
using TalkTrim.Entities.Video;

namespace TalkTrim.Services;

/// <summary>
/// 从队列取出视频项目任务并在独立 Scope 中执行。
/// </summary>
public sealed class ProjectJobBackgroundWorker : BackgroundService
{
    private readonly ProjectJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProjectJobBackgroundWorker> _logger;

    public ProjectJobBackgroundWorker(
        ProjectJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<ProjectJobBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("视频项目后台任务 Worker 已启动。");
        await RecoverInterruptedJobsAsync(stoppingToken);

        await foreach (var jobId in _queue.ReadAllAsync(stoppingToken))
        {
            _logger.LogDebug("从队列取出任务：JobId={JobId}", jobId);
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<ProjectJobExecutor>();
                await executor.ExecuteAsync(jobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理后台任务异常：JobId={JobId}", jobId);
            }
        }
    }

    private async Task RecoverInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var freeSql = scope.ServiceProvider.GetRequiredService<IFreeSql>();

        var interrupted = await freeSql.Select<ProjectJob>()
            .Where(j => j.Status == ProjectJobStatus.Pending || j.Status == ProjectJobStatus.Running)
            .OrderBy(j => j.Id)
            .ToListAsync(cancellationToken);

        foreach (var job in interrupted)
        {
            if (job.Status == ProjectJobStatus.Running)
            {
                await freeSql.Update<ProjectJob>()
                    .Where(j => j.Id == job.Id)
                    .Set(j => j.Status, ProjectJobStatus.Pending)
                    .Set(j => j.ProgressMessage, "服务重启后重新排队…")
                    .ExecuteAffrowsAsync(cancellationToken);
            }

            await _queue.EnqueueAsync(job.Id, cancellationToken);
            _logger.LogInformation("恢复未完成任务：JobId={JobId}, ProjectId={ProjectId}", job.Id, job.ProjectId);
        }
    }
}
