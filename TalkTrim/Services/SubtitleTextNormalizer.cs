namespace TalkTrim.Services;

/// <summary>
/// 识别/导出字幕时的文案整理：去掉中文行内空格、去掉句末标点。
/// </summary>
public static class SubtitleTextNormalizer
{
    private static readonly HashSet<char> TrailingPunctuation = new(
        ".。,，、;；:：!！?？…—～~'\"”’】》）)]}」』");

    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text.Trim();
        if (ContainsCjk(result))
        {
            result = string.Concat(result.Where(c => !char.IsWhiteSpace(c)));
        }

        return TrimTrailingPunctuation(result);
    }

    private static string TrimTrailingPunctuation(string text)
    {
        var result = text;
        while (result.Length > 0 && TrailingPunctuation.Contains(result[^1]))
        {
            result = result[..^1];
        }

        return result;
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if (ch is >= '\u4e00' and <= '\u9fff')
            {
                return true;
            }
        }

        return false;
    }
}
