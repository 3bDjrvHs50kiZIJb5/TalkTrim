namespace TalkTrim.Services;

/// <summary>
/// 字幕压制可选字体（ASS Fontname）。
/// </summary>
public static class SubtitleFontOptions
{
    public static IReadOnlyList<(string Value, string Label)> Items { get; } =
    [
        ("抖音美好体", "抖音美好体（中文默认）"),
        ("DouyinSans", "DouyinSans（英文默认）"),
    ];
}
