namespace TalkTrim.Models.Subtitle;

public sealed class SubtitleWord
{
    public string Text { get; set; } = string.Empty;
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public string? Punctuation { get; set; }
}
