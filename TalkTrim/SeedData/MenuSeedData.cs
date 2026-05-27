using FreeSql;
using NeoAdmin.Blazor.Entities;
using BlazorMenuSeedData = NeoAdmin.Blazor.SeedData.MenuSeedData;

namespace TalkTrim.SeedData;

/// <summary>
/// 博客管理菜单种子数据（宿主项目专用，非系统菜单可在后台删改）。
/// </summary>
public static class MenuSeedData
{
    private const bool IsSystem = false;

    public static void Ensure(IFreeSql freeSql)
    {
        BlazorMenuSeedData.EnsureMenus(freeSql, CreateMenus());
    }

    private static List<SysMenu> CreateMenus() =>
    [
        BlazorMenuSeedData.Menu("视频管理", "video", string.Empty, 0, SysMenuSidebarStyle.展开,
        [
            BlazorMenuSeedData.Page("项目管理", "/Video/Project", 461, "folder", IsSystem)
        ], isSystem: IsSystem),

        BlazorMenuSeedData.Menu("博客管理", "newspaper", string.Empty, 45, SysMenuSidebarStyle.展开,
        [
            BlazorMenuSeedData.Page("分类", "/Blog/Classify", 451, "folder", IsSystem),
            BlazorMenuSeedData.Page("频道", "/Blog/Channel", 452, "rss", IsSystem),
            BlazorMenuSeedData.PageWithAudit("文章", "/Blog/Article", 453, "file-text", IsSystem),
            BlazorMenuSeedData.Page("标签", "/Blog/Tag2", 454, "tags", IsSystem),
            BlazorMenuSeedData.Page("评论", "/Blog/Comment", 455, "message-circle", IsSystem),
            BlazorMenuSeedData.Page("用户点赞", "/Blog/UserLike", 456, "thumbs-up", IsSystem),
            BlazorMenuSeedData.Page("收藏", "/Blog/Collection", 457, "bookmark", IsSystem)
        ], isSystem: IsSystem),

        BlazorMenuSeedData.Menu("Api", "code", string.Empty, 0, SysMenuSidebarStyle.收起,
        [
            BlazorMenuSeedData.Menu("Login", "log-in", "login", 100, children:
            [
                BlazorMenuSeedData.Api("Register", "user-plus", "Register", 101, IsSystem),
                BlazorMenuSeedData.Api("GetWhoIsUsingList", "users", "GetWhoIsUsingList", 102, IsSystem),
                BlazorMenuSeedData.Api("Login", "log-in", "Login", 103, IsSystem),
                BlazorMenuSeedData.Api("Logout", "log-out", "Logout", 104, IsSystem),
                BlazorMenuSeedData.Api("Check", "circle-check", "Check", 105, IsSystem),
                BlazorMenuSeedData.Api("UpdateMemberInfo", "user-pen", "UpdateMemberInfo", 106, IsSystem),
                BlazorMenuSeedData.Api("ChangePassword", "key-round", "ChangePassword", 107, IsSystem),
                BlazorMenuSeedData.Api("DeleteAccount", "user-x", "DeleteAccount", 108, IsSystem),
                BlazorMenuSeedData.Api("UploadAvatar", "image", "UploadAvatar", 109, IsSystem),
                BlazorMenuSeedData.Api("UploadBadgePhoto", "badge", "UploadBadgePhoto", 110, IsSystem),
                BlazorMenuSeedData.Api("SendResetPasswordCode", "mail", "SendResetPasswordCode", 111, IsSystem),
                BlazorMenuSeedData.Api("ResetPassword", "unlock-keyhole", "ResetPassword", 112, IsSystem),
                BlazorMenuSeedData.Api("SetAIAlarmLevel", "bot", "SetAIAlarmLevel", 113, IsSystem)
            ], type: SysMenuType.接口, isSystem: IsSystem),
            BlazorMenuSeedData.Menu("Article", "newspaper", "article", 200, children:
            [
                BlazorMenuSeedData.Api("GetAll", "list", "GetAll", 201, IsSystem)
            ], type: SysMenuType.接口, isSystem: IsSystem)
        ], type: SysMenuType.接口, isHidden: true, isSystem: IsSystem)
    ];
}
