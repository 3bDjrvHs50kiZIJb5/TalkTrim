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
}
