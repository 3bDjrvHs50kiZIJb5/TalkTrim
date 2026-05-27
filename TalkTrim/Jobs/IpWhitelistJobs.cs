using FreeScheduler;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NeoAdmin.Blazor.Attributes;
using NeoAdmin.Blazor.Entities;

namespace TalkTrim.Jobs;

/// <summary>
/// IP 白名单定时任务。
/// </summary>
public static class IpWhitelistJobs
{
    /// <summary>
    /// 每 5 分钟将全部 IP 白名单设为禁用（不删除记录）。
    /// </summary>
    [Scheduler("ip-whitelist.clear", "0 */5 * * * *")]
    public static async Task ClearIpWhitelist(IServiceProvider serviceProvider, TaskInfo task)
    {
        IFreeSql freeSql = serviceProvider.GetRequiredService<IFreeSql>();
        ILogger logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IpWhitelistJobs));

        logger.LogInformation("ClearIpWhitelist 开始，TaskId={TaskId}", task.Id);

        int disabled = await freeSql.Update<SysIpWhitelist>()
            .Set(a => a.IsEnabled, false)
            .Where(a => a.IsEnabled)
            .ExecuteAffrowsAsync();

        logger.LogInformation("ClearIpWhitelist 完成，禁用行数={Disabled}", disabled);
    }
}
