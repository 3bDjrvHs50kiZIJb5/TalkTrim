using FreeSql;
using NeoAdmin.Blazor.Core.Identity;
using NeoAdmin.Blazor.Entities;
using NeoAdmin.Blazor.Models;

namespace TalkTrim.Services;

/// <summary>
/// 解析当前登录用户及其是否具备全局数据权限。
/// 系统账号（SysUser.IsSystem）或管理员角色（SysRole.IsAdministrator）均视为管理员。
/// </summary>
public sealed class CurrentUserRoleService
{
    private readonly IFreeSql _freeSql;
    private readonly NeoAdminAuthService _authService;

    public CurrentUserRoleService(IFreeSql freeSql, NeoAdminAuthService authService)
    {
        _freeSql = freeSql;
        _authService = authService;
    }

    public async Task<(long? UserId, bool IsAdministrator)> ResolveAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, false);
        }

        ApiResult<UserSummaryResponse> result = await _authService.CheckAsync(token);
        if (!result.Succeeded || result.Data is null)
        {
            return (null, false);
        }

        long userId = result.Data.Id;
        SysUser? user = await _freeSql.Select<SysUser>()
            .Where(u => u.Id == userId)
            .FirstAsync(cancellationToken);

        if (user is null)
        {
            return (null, false);
        }

        if (user.IsSystem)
        {
            return (userId, true);
        }

        List<long> roleIds = await _freeSql.Select<SysRoleUser>()
            .Where(link => link.UserId == userId)
            .ToListAsync(link => link.RoleId, cancellationToken);

        if (roleIds.Count == 0)
        {
            return (userId, false);
        }

        bool isAdministrator = await _freeSql.Select<SysRole>()
            .Where(role => roleIds.Contains(role.Id) && role.IsAdministrator)
            .AnyAsync(cancellationToken);

        return (userId, isAdministrator);
    }
}
