using System.Globalization;
using System.Text;
using TalkTrim.Models.Subtitle;

namespace TalkTrim.Services;

/// <summary>
/// 生成 ffmpeg ass 滤镜用的 ASS 字幕文件（双语、可配置字体与背景）。
/// </summary>
public static class AssSubtitleBuilder
{
    private const int PlayResX = 1920;
    private const int PlayResY = 1080;
    private const int MarginX = 400;
    /// <summary>距底边像素；alignment=2 时越大越靠上。中文在上。</summary>
    private const int ChineseMarginV = 134;
    /// <summary>英文字幕在下，距底边更近。</summary>
    private const int EnglishMarginV = 78;

    public readonly record struct SubtitleStyle(
        string FontName,
        int FontSize,
        string? FontColorHex,
        string? BackgroundColorHex,
        int BackgroundOpacityPercent,
        int MarginV);

    public static string BuildBilingual(
        IReadOnlyList<SubtitleCue> cues,
        SubtitleStyle english,
        SubtitleStyle chinese)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Script Info]");
        sb.AppendLine("ScriptType: v4.00+");
        sb.AppendLine("WrapStyle: 0");
        sb.AppendLine("ScaledBorderAndShadow: yes");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PlayResX: {PlayResX}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"PlayResY: {PlayResY}");
        sb.AppendLine();
        sb.AppendLine("[V4+ Styles]");
        sb.AppendLine(
            "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        sb.AppendLine(BuildStyleLine("ExportEN", english));
        sb.AppendLine(BuildStyleLine("ExportZH", chinese));
        sb.AppendLine();
        sb.AppendLine("[Events]");
        sb.AppendLine(
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var cue in cues)
        {
            var start = MsToAssTime(cue.StartMs);
            var end = MsToAssTime(cue.EndMs);
            var en = EscapeAssText(cue.Text);
            var zh = EscapeAssText(cue.Translation ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(en))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"Dialogue: 0,{start},{end},ExportEN,,0,0,0,,{en}");
            }

            if (!string.IsNullOrWhiteSpace(zh))
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"Dialogue: 0,{start},{end},ExportZH,,0,0,0,,{zh}");
            }
        }

        return sb.ToString();
    }

    public static SubtitleStyle BuildEnglishStyle(
        string? fontName,
        int fontSize,
        string? fontColorHex,
        string? backgroundColorHex,
        int backgroundOpacityPercent) =>
        new(
            ResolveFontName(fontName),
            ClampFontSize(fontSize, 32),
            fontColorHex,
            backgroundColorHex,
            backgroundOpacityPercent,
            EnglishMarginV);

    public static SubtitleStyle BuildChineseStyle(
        string? fontName,
        int fontSize,
        string? fontColorHex,
        string? backgroundColorHex,
        int backgroundOpacityPercent) =>
        new(
            ResolveFontName(fontName),
            ClampFontSize(fontSize, 44),
            fontColorHex,
            backgroundColorHex,
            backgroundOpacityPercent,
            ChineseMarginV);

    private static string BuildStyleLine(string styleName, SubtitleStyle style)
    {
        var fontSize = ClampFontSize(style.FontSize, 48);
        var primaryColour = ToAssPrimaryColour(style.FontColorHex);
        var backColour = ToAssBackColour(style.BackgroundColorHex, style.BackgroundOpacityPercent);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"Style: {styleName},{style.FontName},{fontSize},{primaryColour},{primaryColour},&H00000000,{backColour},0,0,0,0,100,100,0,0,4,0,0,2,{MarginX},{MarginX},{style.MarginV},1");
    }

    private static string ResolveFontName(string? fontName) =>
        string.IsNullOrWhiteSpace(fontName)
            ? SubtitleFontOptions.Items[0].Value
            : fontName.Trim();

    private static int ClampFontSize(int fontSize, int fallback) =>
        fontSize > 0 ? Math.Clamp(fontSize, 8, 200) : fallback;

    public static List<SubtitleCue> ApplySpeedToCues(
        IReadOnlyList<SubtitleCue> cues,
        decimal speedMultiplier)
    {
        if (speedMultiplier <= 0 || speedMultiplier == 1m)
        {
            return cues.Select(CloneCue).ToList();
        }

        var speed = (double)speedMultiplier;
        return cues.Select(c => new SubtitleCue
        {
            Id = c.Id,
            StartMs = ScaleMs(c.StartMs, speed),
            EndMs = ScaleMs(c.EndMs, speed),
            Text = c.Text,
            Translation = c.Translation,
        }).ToList();
    }

    private static int ScaleMs(int ms, double speed) =>
        (int)Math.Max(0, Math.Round(ms / speed, MidpointRounding.AwayFromZero));

    private static SubtitleCue CloneCue(SubtitleCue cue) =>
        new()
        {
            Id = cue.Id,
            StartMs = cue.StartMs,
            EndMs = cue.EndMs,
            Text = cue.Text,
            Translation = cue.Translation,
        };

    private static string MsToAssTime(int ms)
    {
        var safeMs = Math.Max(0, ms);
        var h = safeMs / 3_600_000;
        var m = (safeMs % 3_600_000) / 60_000;
        var s = (safeMs % 60_000) / 1_000;
        var cs = (safeMs % 1_000) / 10;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{h}:{m:00}:{s:00}.{cs:00}");
    }

    private static string EscapeAssText(string text) =>
        text
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}")
            .Replace("\r\n", "\\N")
            .Replace("\r", "\\N")
            .Replace("\n", "\\N");

    /// <summary>ASS 主色 &amp;HAABBGGRR（字幕文字 PrimaryColour）。</summary>
    private static string ToAssPrimaryColour(string? hex)
    {
        var rgb = ParseHexColor(string.IsNullOrWhiteSpace(hex) ? "#FFFFFF" : hex);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"&H00{rgb.B:X2}{rgb.G:X2}{rgb.R:X2}");
    }

    /// <summary>ASS 颜色 &amp;HAABBGGRR，背景 BorderStyle=4 时使用 BackColour。</summary>
    private static string ToAssBackColour(string? hex, int opacityPercent)
    {
        var rgb = ParseHexColor(string.IsNullOrWhiteSpace(hex) ? "#000000" : hex);
        var alpha = (int)Math.Round((100 - Math.Clamp(opacityPercent, 0, 100)) * 255 / 100.0,
            MidpointRounding.AwayFromZero);
        alpha = Math.Clamp(alpha, 0, 255);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"&H{alpha:X2}{rgb.B:X2}{rgb.G:X2}{rgb.R:X2}");
    }

    private static (byte R, byte G, byte B) ParseHexColor(string? hex)
    {
        var value = (hex ?? "#000000").Trim().TrimStart('#');
        if (value.Length != 6)
        {
            return (0, 0, 0);
        }

        if (!byte.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return (0, 0, 0);
        }

        return (r, g, b);
    }
}
