namespace TalkTrim.Services.DashScope;

/// <summary>
/// 阿里云百炼 DashScope 配置（ASR、字幕翻译，逻辑参考 Youtube_Learner）。
/// </summary>
public sealed class DashScopeOptions
{
    public const string SectionName = "DashScope";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// ASR 语言提示（Paraformer language_hints）。
    /// 中文口播填 ["zh"]，英文口播填 ["en"]，可在 appsettings 的 DashScope:LanguageHints 修改。
    /// </summary>
    public string[] LanguageHints { get; set; } = ["zh"];

    /// <summary>字幕翻译目标语言（英文识别时译成中文；中文识别时自动译成英文）。</summary>
    public string TranslateTarget { get; set; } = "中文";

    /// <summary>主识别语言是否为中文（以 LanguageHints 第一项为准）。</summary>
    public bool IsChineseAsrPrimary =>
        LanguageHints.Length > 0
        && string.Equals(LanguageHints[0], "zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>翻译并发批次数。</summary>
    public int TranslateConcurrency { get; set; } = 3;

    /// <summary>
    /// ASR 完成后按字级时间戳拆分过长句（需 Paraformer 返回 words[]）。
    /// Paraformer 录音 API 无「最大句长」参数，此项为客户端二次处理。
    /// </summary>
    public bool SplitLongCuesEnabled { get; set; } = true;

    /// <summary>超过该词数则尝试拆分（字级单元，中文多为单字）。</summary>
    public int CueSplitMaxWords { get; set; } = 18;

    /// <summary>超过该时长（毫秒）则尝试拆分。</summary>
    public int CueSplitMaxDurationMs { get; set; } = 8000;

    /// <summary>无字级时间戳时，按标点兜底拆分的最大字符数。</summary>
    public int CueSplitFallbackMaxChars { get; set; } = 42;

    /// <summary>过滤语气词（Paraformer disfluency_removal_enabled）。</summary>
    public bool DisfluencyRemovalEnabled { get; set; }
}
