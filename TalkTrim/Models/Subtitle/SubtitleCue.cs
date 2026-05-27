namespace TalkTrim.Models.Subtitle;

public sealed class SubtitleCue
{
    public int Id { get; set; }
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Translation { get; set; }
    public List<SubtitleWord>? Words { get; set; }
}
