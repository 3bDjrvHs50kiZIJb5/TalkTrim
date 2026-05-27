using System.Globalization;
using System.Text;
using TalkTrim.Models.Subtitle;

namespace TalkTrim.Services;

public static class SubtitleSrtFormatter
{
    public static string ToSrt(IReadOnlyList<SubtitleCue> cues, bool chineseLines)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
        {
            var cue = cues[i];
            var text = chineseLines ? (cue.Translation ?? cue.Text) : cue.Text;
            text = SubtitleTextNormalizer.Normalize(text);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            sb.Append(i + 1);
            sb.Append('\n');
            sb.Append(MsToSrtTime(cue.StartMs));
            sb.Append(" --> ");
            sb.Append(MsToSrtTime(cue.EndMs));
            sb.Append('\n');
            sb.Append(text);
            sb.Append("\n\n");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>双语 SRT：每段两行，上行英文、下行中文。</summary>
    public static string ToBilingualSrt(IReadOnlyList<SubtitleCue> cues)
    {
        var sb = new StringBuilder();
        var index = 1;
        foreach (var cue in cues)
        {
            var en = SubtitleTextNormalizer.Normalize(cue.Text);
            var zh = SubtitleTextNormalizer.Normalize(cue.Translation);
            if (string.IsNullOrWhiteSpace(en))
            {
                continue;
            }

            sb.Append(index++);
            sb.Append('\n');
            sb.Append(MsToSrtTime(cue.StartMs));
            sb.Append(" --> ");
            sb.Append(MsToSrtTime(cue.EndMs));
            sb.Append('\n');
            sb.Append(en);
            if (!string.IsNullOrWhiteSpace(zh))
            {
                sb.Append('\n');
                sb.Append(zh);
            }

            sb.Append("\n\n");
        }

        return sb.ToString().TrimEnd();
    }

    public static string JoinScriptLines(IReadOnlyList<SubtitleCue> cues) =>
        string.Join(
            Environment.NewLine,
            cues.Select(c => SubtitleTextNormalizer.Normalize(c.Text))
                .Where(t => !string.IsNullOrWhiteSpace(t)));

    private static string MsToSrtTime(int ms)
    {
        var h = ms / 3_600_000;
        var m = (ms % 3_600_000) / 60_000;
        var s = (ms % 60_000) / 1_000;
        var mss = ms % 1_000;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{h:00}:{m:00}:{s:00},{mss:000}");
    }
}
