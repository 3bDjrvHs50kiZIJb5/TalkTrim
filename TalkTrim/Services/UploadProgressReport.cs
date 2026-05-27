namespace TalkTrim.Services;

/// <summary>浏览器文件上传进度（百分比 0–100）。</summary>
public readonly record struct UploadProgressReport(int Percent, string? Message = null);
