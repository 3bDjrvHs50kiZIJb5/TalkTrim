using System.Collections.Concurrent;

namespace TalkTrim.Services;

/// <summary>
/// 跟踪运行中后台任务的取消令牌。
/// </summary>
public sealed class ProjectJobCancellationRegistry
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _sources = new();

    public CancellationToken Register(long jobId)
    {
        var cts = new CancellationTokenSource();
        _sources[jobId] = cts;
        return cts.Token;
    }

    public void Unregister(long jobId)
    {
        if (_sources.TryRemove(jobId, out var cts))
        {
            cts.Dispose();
        }
    }

    public bool TryCancel(long jobId)
    {
        if (_sources.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return true;
        }

        return false;
    }
}
