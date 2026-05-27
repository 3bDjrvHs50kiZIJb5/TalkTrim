namespace TalkTrim.Services;

/// <summary>
/// 将相对媒体路径转为绝对 URL（与 NeoAdmin NavigationManager.ToMediaUrl 行为一致）。
/// </summary>
public static class MediaUrlHelper
{
    public static string? ToAbsoluteMediaUrl(string? url, string siteBaseUri)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (string.IsNullOrWhiteSpace(siteBaseUri))
        {
            return null;
        }

        var baseUri = siteBaseUri.TrimEnd('/') + "/";
        return new Uri(new Uri(baseUri), url.TrimStart('/')).ToString();
    }

    /// <summary>
    /// 将本站相对媒体路径（如 /uploads/video/a.mp4）解析为 wwwroot 下的物理路径。
    /// </summary>
    public static bool TryResolveWebRootPhysicalPath(string? url, string webRootPath, out string physicalPath)
    {
        physicalPath = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(webRootPath))
        {
            return false;
        }

        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Path.IsPathRooted(trimmed) && File.Exists(trimmed))
        {
            physicalPath = trimmed;
            return true;
        }

        var relative = trimmed.TrimStart('/');
        physicalPath = Path.Combine(webRootPath, relative);
        return File.Exists(physicalPath);
    }
}
