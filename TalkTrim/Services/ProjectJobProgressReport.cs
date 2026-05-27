namespace TalkTrim.Services;

/// <summary>
/// 后台任务进度（文案 + 0–100 百分比）。
/// </summary>
public sealed record ProjectJobProgressReport(string Message, int Percent);
