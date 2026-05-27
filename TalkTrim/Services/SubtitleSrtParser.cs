using System.Globalization;
using System.Text.RegularExpressions;
using TalkTrim.Models.Subtitle;

namespace TalkTrim.Services;

/// <summary>
/// 解析 SRT 字幕为 <see cref="SubtitleCue"/> 列表。
/// </summary>
public static partial class SubtitleSrtParser
{
    private static readonly Regex TimeLineRegex = TimeLineRegexFactory();

    public static List<SubtitleCue> Parse(string? srt)
    {
        if (string.IsNullOrWhiteSpace(srt))
        {
            return [];
        }

        var blocks = srt.Trim().Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var cues = new List<SubtitleCue>();
        var id = 1;

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.TrimEntries);
            if (lines.Length < 2)
            {
                continue;
            }

            var timeLineIndex = Array.FindIndex(lines, l => TimeLineRegex.IsMatch(l));
            if (timeLineIndex < 0 || timeLineIndex + 1 >= lines.Length)
            {
                continue;
            }

            var match = TimeLineRegex.Match(lines[timeLineIndex]);
            if (!match.Success)
            {
                continue;
            }

            var startMs = ParseSrtTime(match.Groups[1].Value);
            var endMs = ParseSrtTime(match.Groups[2].Value);
            var textLines = lines[(timeLineIndex + 1)..];
            if (textLines.Length == 0)
            {
                continue;
            }

            cues.Add(new SubtitleCue
            {
                Id = id++,
                StartMs = startMs,
                EndMs = endMs,
                Text = textLines[0],
                Translation = textLines.Length > 1 ? string.Join('\n', textLines[1..]) : null,
            });
        }

        return cues;
    }

    /// <summary>
    /// 合并中英文字幕字段为双语 cue（英文 Text，中文 Translation）。
    /// </summary>
    public static List<SubtitleCue> MergeBilingual(string? englishSrt, string? chineseSrt)
    {
        var english = Parse(englishSrt);
        var chinese = Parse(chineseSrt);

        if (english.Count == 0 && chinese.Count == 0)
        {
            return [];
        }

        if (english.Count == 0)
        {
            return chinese.Select(c => new SubtitleCue
            {
                Id = c.Id,
                StartMs = c.StartMs,
                EndMs = c.EndMs,
                Text = string.Empty,
                Translation = string.IsNullOrWhiteSpace(c.Translation)
                    ? c.Text
                    : $"{c.Text}\n{c.Translation}".Trim(),
            }).ToList();
        }

        if (chinese.Count == 0)
        {
            return english;
        }

        var merged = new List<SubtitleCue>();
        var count = Math.Max(english.Count, chinese.Count);
        for (var i = 0; i < count; i++)
        {
            var en = i < english.Count ? english[i] : english[^1];
            var zh = i < chinese.Count ? chinese[i] : chinese[^1];
            merged.Add(new SubtitleCue
            {
                Id = i + 1,
                StartMs = en.StartMs > 0 ? en.StartMs : zh.StartMs,
                EndMs = en.EndMs > 0 ? en.EndMs : zh.EndMs,
                Text = en.Text.Trim(),
                Translation = GetChineseLine(zh),
            });
        }

        return merged;
    }

    private static string GetChineseLine(SubtitleCue cue)
    {
        if (!string.IsNullOrWhiteSpace(cue.Translation))
        {
            return cue.Translation.Trim();
        }

        return cue.Text.Trim();
    }

    private static int ParseSrtTime(string value)
    {
        var normalized = value.Trim().Replace(',', '.');
        if (!TimeSpan.TryParse(normalized, CultureInfo.InvariantCulture, out var ts))
        {
            return 0;
        }

        return (int)Math.Round(ts.TotalMilliseconds, MidpointRounding.AwayFromZero);
    }

    [GeneratedRegex(
        @"(\d{1,2}:\d{2}:\d{2}[,.]\d{3})\s*-->\s*(\d{1,2}:\d{2}:\d{2}[,.]\d{3})",
        RegexOptions.Compiled)]
    private static partial Regex TimeLineRegexFactory();
}
