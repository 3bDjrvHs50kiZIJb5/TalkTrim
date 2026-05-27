using System.Threading.Channels;

namespace TalkTrim.Services;

/// <summary>
/// 视频项目后台任务队列（进程内 Channel）。
/// </summary>
public sealed class ProjectJobQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>(
        new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(long jobId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public IAsyncEnumerable<long> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
