using FreeSql;
using NeoAdmin.Blazor.Entities;

namespace TalkTrim.SeedData;

/// <summary>
/// 将站点标题设为 TalkTrim（新库写入；旧库若仍为默认 NeoAdmin 则升级）。
/// </summary>
public static class SiteSettingsSeedData
{
    private const string AppTitle = "TalkTrim";
    private const string LegacyTitle = "NeoAdmin";

    public static void Ensure(IFreeSql freeSql)
    {
        var settings = freeSql.Select<SysSiteSettings>()
            .OrderBy(a => a.Id)
            .First();

        if (settings is null)
        {
            freeSql.Insert(new SysSiteSettings
            {
                Title = AppTitle,
                Host = "localhost",
                Host2 = "127.0.0.1",
                Description = "TalkTrim 口播视频管理",
                Logo = "/_content/NeoAdmin.Blazor/images/logo.png",
                LoginImage = "/_content/NeoAdmin.Blazor/images/login_bg.png",
                IsEnabled = true,
            }).ExecuteAffrows();
            return;
        }

        if (!string.Equals(settings.Title, LegacyTitle, StringComparison.Ordinal))
        {
            return;
        }

        freeSql.Update<SysSiteSettings>()
            .Where(a => a.Id == settings.Id)
            .Set(a => a.Title, AppTitle)
            .Set(a => a.Description, "TalkTrim 口播视频管理")
            .ExecuteAffrows();
    }
}
