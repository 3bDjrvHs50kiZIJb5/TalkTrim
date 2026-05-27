using System.Text.RegularExpressions;

namespace TalkTrim.Services;

/// <summary>
/// 编辑页字幕区的纯文本工具（不写库）：去语气词、全文替换。
/// </summary>
public static partial class SubtitleSrtTextTools
{
    /// <summary>中文口语填充词，长词在前以免误删子串。</summary>
    private static readonly string[] ChineseFillerTokens =
    [
        "你知道吧", "对不对", "就是说", "怎么说呢", "所以说", "其实呢", "那么呢", "然后呢",
        "对吧", "那个", "这个", "嗯嗯", "啊啊", "呃呃", "嘿嘿", "哈哈",
        "嗯", "啊", "呀", "哇", "呃", "额", "哦", "噢", "喔", "唉", "诶", "欸",
        "嘿", "哈", "咯", "喽", "呐", "哩", "哟", "嘛",
    ];

    /// <summary>英文口语填充词，长词在前。</summary>
    private static readonly string[] EnglishFillerTokens =
    [
        "you know", "i mean", "sort of", "kind of", "uh huh", "um hmm", "uh-huh", "um-hmm",
        "umm", "uhh", "err", "ahh", "ohh", "hmm", "mmm",
        "um", "uh", "er", "ah", "oh", "hm", "mm", "eh", "ha", "huh", "well", "like",
    ];

    public static string RemoveFillerWords(string? srt, bool chinese)
    {
        if (string.IsNullOrWhiteSpace(srt))
        {
            return srt ?? string.Empty;
        }

        var cues = SubtitleSrtParser.Parse(srt);
        if (cues.Count == 0)
        {
            return RemoveFillerWordsFromPlainSrt(srt, chinese);
        }

        foreach (var cue in cues)
        {
            cue.Text = RemoveFillerWordsFromLine(cue.Text, chinese);
            if (!string.IsNullOrEmpty(cue.Translation))
            {
                cue.Translation = RemoveFillerWordsFromLine(cue.Translation, chinese);
            }
        }

        return SubtitleSrtFormatter.ToSrt(cues, chineseLines: chinese);
    }

    public static (string Text, int Count) ReplaceAll(string? text, string find, string replace)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(find))
        {
            return (text ?? string.Empty, 0);
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(find, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += find.Length;
        }

        if (count == 0)
        {
            return (text, 0);
        }

        return (text.Replace(find, replace ?? string.Empty, StringComparison.Ordinal), count);
    }

    private static string RemoveFillerWordsFromPlainSrt(string srt, bool chinese)
    {
        var lines = srt.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (IsSrtTimeLine(line) || IsCueIndexLine(line))
            {
                continue;
            }

            lines[i] = RemoveFillerWordsFromLine(line, chinese);
        }

        return string.Join('\n', lines);
    }

    private static bool IsCueIndexLine(string line)
    {
        return line.Trim().Length > 0 && line.Trim().All(char.IsDigit);
    }

    private static bool IsSrtTimeLine(string line) => line.Contains("-->", StringComparison.Ordinal);

    private static string RemoveFillerWordsFromLine(string? line, bool chinese)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return line ?? string.Empty;
        }

        var result = line;
        var tokens = chinese ? ChineseFillerTokens : EnglishFillerTokens;
        var comparison = chinese ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        foreach (var token in tokens)
        {
            result = ReplaceToken(result, token, comparison);
        }

        return CleanupLine(result, chinese);
    }

    private static string ReplaceToken(string text, string token, StringComparison comparison)
    {
        var index = 0;
        while ((index = text.IndexOf(token, index, comparison)) >= 0)
        {
            var beforeOk = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var afterIndex = index + token.Length;
            var afterOk = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (!beforeOk || !afterOk)
            {
                index += token.Length;
                continue;
            }

            text = text.Remove(index, token.Length);
        }

        return text;
    }

    private static string CleanupLine(string text, bool chinese)
    {
        if (chinese)
        {
            text = DuplicatePunctuationRegex().Replace(text, "$1");
            text = text.Trim('，', ',', '、', '。', '.', ' ', '　');
        }
        else
        {
            text = MultipleSpacesRegex().Replace(text, " ");
            text = SpaceBeforePunctuationRegex().Replace(text, "$1");
            text = text.Trim(' ', ',', '.');
        }

        return text.Trim();
    }

    [GeneratedRegex(@"([，,、])\1+", RegexOptions.Compiled)]
    private static partial Regex DuplicatePunctuationRegex();

    [GeneratedRegex(@" {2,}", RegexOptions.Compiled)]
    private static partial Regex MultipleSpacesRegex();

    [GeneratedRegex(@"\s+([,.!?])", RegexOptions.Compiled)]
    private static partial Regex SpaceBeforePunctuationRegex();
}
