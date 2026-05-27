using TalkTrim.Models.Subtitle;

namespace TalkTrim.Services;

/// <summary>
/// 去口气：根据字幕时间轴裁切句间过长停顿，并同步前移字幕时间。
/// </summary>
public static class BreathTrimService
{
    public sealed record RemovalSegment(int StartMs, int EndMs)
    {
        public int DurationMs => EndMs - StartMs;
    }

    /// <summary>
    /// 句间最多保留的间隔（秒）；0 或负数表示不处理。
    /// </summary>
    public static bool IsEnabled(decimal maxGapSeconds) => maxGapSeconds > 0;

    /// <summary>
    /// 根据字幕间隙计算原时间轴上需要裁掉的区间（用于 ffmpeg 等）。
    /// </summary>
    public static IReadOnlyList<RemovalSegment> BuildRemovalSegments(
        IReadOnlyList<SubtitleCue> cues,
        decimal maxGapSeconds)
    {
        if (!IsEnabled(maxGapSeconds) || cues.Count == 0)
        {
            return [];
        }

        var maxGapMs = (int)Math.Round(maxGapSeconds * 1000m, MidpointRounding.AwayFromZero);
        var speech = CollectSpeechIntervals(cues);
        if (speech.Count < 2)
        {
            return [];
        }

        var removals = new List<RemovalSegment>();
        for (var i = 0; i < speech.Count - 1; i++)
        {
            var gapMs = speech[i + 1].StartMs - speech[i].EndMs;
            if (gapMs <= maxGapMs)
            {
                continue;
            }

            var trimStart = speech[i].EndMs + maxGapMs;
            var trimEnd = speech[i + 1].StartMs;
            if (trimEnd > trimStart)
            {
                removals.Add(new RemovalSegment(trimStart, trimEnd));
            }
        }

        return removals;
    }

    /// <summary>
    /// 按去口气规则前移字幕（含词级时间戳）。
    /// </summary>
    public static List<SubtitleCue> ShiftCues(
        IReadOnlyList<SubtitleCue> cues,
        decimal maxGapSeconds)
    {
        var removals = BuildRemovalSegments(cues, maxGapSeconds);
        if (removals.Count == 0)
        {
            return cues.Select(CloneCue).ToList();
        }

        return cues.Select(c => ShiftCue(c, removals)).ToList();
    }

    public static int ShiftMs(int originalMs, IReadOnlyList<RemovalSegment> removals)
    {
        var shift = 0;
        foreach (var segment in removals)
        {
            if (segment.StartMs >= originalMs)
            {
                break;
            }

            if (originalMs >= segment.EndMs)
            {
                shift += segment.DurationMs;
            }
            else if (originalMs > segment.StartMs)
            {
                shift += originalMs - segment.StartMs;
            }
        }

        return Math.Max(0, originalMs - shift);
    }

    private static SubtitleCue ShiftCue(SubtitleCue cue, IReadOnlyList<RemovalSegment> removals)
    {
        var shifted = CloneCue(cue);
        shifted.StartMs = ShiftMs(cue.StartMs, removals);
        shifted.EndMs = ShiftMs(cue.EndMs, removals);
        if (cue.Words is { Count: > 0 })
        {
            shifted.Words = cue.Words
                .Select(w => new SubtitleWord
                {
                    Text = w.Text,
                    Punctuation = w.Punctuation,
                    StartMs = ShiftMs(w.StartMs, removals),
                    EndMs = ShiftMs(w.EndMs, removals),
                })
                .ToList();
        }

        return shifted;
    }

    private static SubtitleCue CloneCue(SubtitleCue cue) =>
        new()
        {
            Id = cue.Id,
            StartMs = cue.StartMs,
            EndMs = cue.EndMs,
            Text = cue.Text,
            Translation = cue.Translation,
            Words = cue.Words?.ToList(),
        };

    private static List<(int StartMs, int EndMs)> CollectSpeechIntervals(IReadOnlyList<SubtitleCue> cues)
    {
        var intervals = new List<(int StartMs, int EndMs)>();
        foreach (var cue in cues)
        {
            if (cue.Words is { Count: > 0 })
            {
                foreach (var word in cue.Words)
                {
                    if (word.EndMs > word.StartMs)
                    {
                        intervals.Add((word.StartMs, word.EndMs));
                    }
                }

                continue;
            }

            if (cue.EndMs > cue.StartMs)
            {
                intervals.Add((cue.StartMs, cue.EndMs));
            }
        }

        intervals.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        return MergeIntervals(intervals);
    }

    private static List<(int StartMs, int EndMs)> MergeIntervals(List<(int StartMs, int EndMs)> intervals)
    {
        if (intervals.Count == 0)
        {
            return intervals;
        }

        var merged = new List<(int StartMs, int EndMs)> { intervals[0] };
        for (var i = 1; i < intervals.Count; i++)
        {
            var last = merged[^1];
            var current = intervals[i];
            if (current.StartMs <= last.EndMs)
            {
                merged[^1] = (last.StartMs, Math.Max(last.EndMs, current.EndMs));
            }
            else
            {
                merged.Add(current);
            }
        }

        return merged;
    }
}
