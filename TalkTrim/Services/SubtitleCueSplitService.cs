using System.Text;
using TalkTrim.Models.Subtitle;

namespace TalkTrim.Services;

/// <summary>
/// ASR 句级结果二次处理：按字级时间戳拆分过长字幕（逻辑参考 Youtube_Learner subtitle.ts）。
/// </summary>
public static partial class SubtitleCueSplitService
{
    private const int DefaultMaxDurationMs = 8000;
    private const int DefaultMaxWords = 18;
    private const int DefaultTargetWords = 12;
    private const int DefaultMinWords = 4;
    private const int DefaultMinDurationMs = 1200;
    private const int StrongPauseMs = 700;
    private const int WeakPauseMs = 500;
    private const int DefaultFallbackMaxChars = 42;

    private static readonly HashSet<char> StrongSplitPunctuation = ['.', '?', '!', '。', '？', '！'];
    private static readonly HashSet<char> WeakSplitPunctuation = [',', ';', ':', '，', '；', '：'];

    public sealed class SplitOptions
    {
        public int MaxDurationMs { get; init; } = DefaultMaxDurationMs;
        public int MaxWords { get; init; } = DefaultMaxWords;
        public int TargetWords { get; init; } = DefaultTargetWords;
        public int MinWords { get; init; } = DefaultMinWords;
        public int MinDurationMs { get; init; } = DefaultMinDurationMs;
        public int FallbackMaxChars { get; init; } = DefaultFallbackMaxChars;
    }

    /// <summary>
    /// 拆分过长 cue 并重新编号。
    /// </summary>
    public static List<SubtitleCue> SplitLongCues(
        IReadOnlyList<SubtitleCue> cues,
        SplitOptions? options = null)
    {
        var opt = options ?? new SplitOptions();
        var result = cues
            .SelectMany(c => SplitCue(c, opt))
            .OrderBy(c => c.StartMs)
            .ToList();

        for (var i = 0; i < result.Count; i++)
        {
            result[i].Id = i;
        }

        return result;
    }

    private static IEnumerable<SubtitleCue> SplitCue(SubtitleCue cue, SplitOptions opt)
    {
        var words = GetTimedWords(cue.Words);
        if (words.Count > 0)
        {
            return SplitCueByWords(cue, words, opt);
        }

        return SplitCueByTextOnly(cue, opt);
    }

    private static IEnumerable<SubtitleCue> SplitCueByWords(
        SubtitleCue cue,
        List<SubtitleWord> words,
        SplitOptions opt)
    {
        var aligned = MakeCueFromWords(cue, words);
        if (!ShouldSplit(words, aligned.StartMs, aligned.EndMs, opt))
        {
            yield return aligned;
            yield break;
        }

        var splitIndex = FindBestSplitIndex(words, opt);
        if (splitIndex is null)
        {
            yield return aligned;
            yield break;
        }

        foreach (var part in SplitCueByWords(cue, words.Take(splitIndex.Value).ToList(), opt))
        {
            yield return part;
        }

        foreach (var part in SplitCueByWords(cue, words.Skip(splitIndex.Value).ToList(), opt))
        {
            yield return part;
        }
    }

  /// <summary>无字级时间戳时，按标点与字数兜底拆分并按时长比例分配时间轴。</summary>
    private static IEnumerable<SubtitleCue> SplitCueByTextOnly(SubtitleCue cue, SplitOptions opt)
    {
        var text = cue.Text.Trim();
        if (text.Length <= opt.FallbackMaxChars)
        {
            yield return cue;
            yield break;
        }

        var parts = SplitTextByPunctuation(text, opt.FallbackMaxChars);
        if (parts.Count <= 1)
        {
            yield return cue;
            yield break;
        }

        var totalChars = parts.Sum(p => p.Length);
        var duration = Math.Max(1, cue.EndMs - cue.StartMs);
        var cursor = cue.StartMs;

        for (var i = 0; i < parts.Count; i++)
        {
            var partDuration = i == parts.Count - 1
                ? cue.EndMs - cursor
                : (int)Math.Round(duration * (parts[i].Length / (double)totalChars));
            partDuration = Math.Max(1, partDuration);

            var endMs = i == parts.Count - 1 ? cue.EndMs : cursor + partDuration;
            yield return new SubtitleCue
            {
                Id = cue.Id,
                StartMs = cursor,
                EndMs = endMs,
                Text = parts[i],
            };
            cursor = endMs;
        }
    }

    private static bool ShouldSplit(
        IReadOnlyList<SubtitleWord> words,
        int startMs,
        int endMs,
        SplitOptions opt) =>
        endMs - startMs > opt.MaxDurationMs || words.Count > opt.MaxWords;

    private static int? FindBestSplitIndex(IReadOnlyList<SubtitleWord> words, SplitOptions opt)
    {
        if (words.Count < opt.MinWords * 2)
        {
            return null;
        }

        var bestIndex = -1;
        var bestScore = int.MinValue;
        for (var i = opt.MinWords; i <= words.Count - opt.MinWords; i++)
        {
            var score = ScoreSplit(words, i, opt);
            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        return bestIndex > 0 ? bestIndex : null;
    }

    private static int ScoreSplit(IReadOnlyList<SubtitleWord> words, int splitIndex, SplitOptions opt)
    {
        var prev = words[splitIndex - 1];
        var next = words[splitIndex];
        var punctuation = GetWordTrailingPunctuation(prev);
        var gapMs = Math.Max(0, next.StartMs - prev.EndMs);
        var leftWords = splitIndex;
        var targetWords = Math.Min(opt.TargetWords, (int)Math.Round(words.Count / 2.0));
        var leftDuration = prev.EndMs - words[0].StartMs;
        var rightDuration = words[^1].EndMs - next.StartMs;

        var score = 0;
        if (StrongSplitPunctuation.Contains(punctuation))
        {
            score += 100;
        }
        else if (WeakSplitPunctuation.Contains(punctuation))
        {
            score += 60;
        }

        if (gapMs >= StrongPauseMs)
        {
            score += 80;
        }
        else if (gapMs >= WeakPauseMs)
        {
            score += 50;
        }

        score += Math.Max(0, 30 - Math.Abs(leftWords - targetWords) * 4);
        score += Math.Max(0, 20 - Math.Abs(leftWords - (words.Count - splitIndex)) * 2);

        if (leftDuration < opt.MinDurationMs)
        {
            score -= 80;
        }

        if (rightDuration < opt.MinDurationMs)
        {
            score -= 80;
        }

        return score;
    }

    private static SubtitleCue MakeCueFromWords(SubtitleCue source, IReadOnlyList<SubtitleWord> words)
    {
        var timedWords = GetTimedWords(words);
        if (timedWords.Count == 0)
        {
            return source;
        }

        var startMs = timedWords[0].StartMs;
        var endMs = timedWords[^1].EndMs;
        return new SubtitleCue
        {
            Id = source.Id,
            StartMs = startMs,
            EndMs = Math.Max(startMs + 1, endMs),
            Text = WordsToText(timedWords),
            Translation = source.Translation,
            Words = timedWords,
        };
    }

    private static List<SubtitleWord> GetTimedWords(IEnumerable<SubtitleWord>? words) =>
        words?
            .Where(w => w.EndMs >= w.StartMs && !string.IsNullOrWhiteSpace(w.Text))
            .OrderBy(w => w.StartMs)
            .ToList() ?? [];

    private static string WordsToText(IReadOnlyList<SubtitleWord> words)
    {
        var parts = words.Select(RenderWordText).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (parts.Count == 0)
        {
            return string.Empty;
        }

        if (parts.Any(ContainsCjk))
        {
            return string.Concat(parts);
        }

        return string.Join(' ', parts).Replace("  ", " ", StringComparison.Ordinal).Trim();
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if (ch >= '\u4e00' && ch <= '\u9fff')
            {
                return true;
            }
        }

        return false;
    }

    private static string RenderWordText(SubtitleWord word)
    {
        var text = word.Text.Trim();
        var punctuation = (word.Punctuation ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(punctuation)
            || StrongSplitPunctuation.Contains(text[^1])
            || WeakSplitPunctuation.Contains(text[^1]))
        {
            return text;
        }

        return text + punctuation;
    }

    private static char GetWordTrailingPunctuation(SubtitleWord word)
    {
        var punctuation = (word.Punctuation ?? string.Empty).Trim();
        if (punctuation.Length > 0)
        {
            return punctuation[^1];
        }

        var text = word.Text.Trim();
        if (text.Length == 0)
        {
            return '\0';
        }

        var last = text[^1];
        return StrongSplitPunctuation.Contains(last) || WeakSplitPunctuation.Contains(last) ? last : '\0';
    }

    private static List<string> SplitTextByPunctuation(string text, int maxChars)
    {
        var parts = new List<string>();
        var buffer = new StringBuilder();

        foreach (var ch in text)
        {
            buffer.Append(ch);
            var isBreak = StrongSplitPunctuation.Contains(ch) || WeakSplitPunctuation.Contains(ch);
            if (isBreak && buffer.Length >= Math.Min(maxChars / 2, 8))
            {
                parts.Add(buffer.ToString().Trim());
                buffer.Clear();
                continue;
            }

            if (buffer.Length >= maxChars && isBreak)
            {
                parts.Add(buffer.ToString().Trim());
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            var tail = buffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(tail))
            {
                parts.Add(tail);
            }
        }

        if (parts.Count <= 1)
        {
            return ChunkByLength(text, maxChars);
        }

        return parts;
    }

    private static List<string> ChunkByLength(string text, int maxChars)
    {
        var parts = new List<string>();
        for (var i = 0; i < text.Length; i += maxChars)
        {
            var len = Math.Min(maxChars, text.Length - i);
            parts.Add(text.Substring(i, len).Trim());
        }

        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }
}
