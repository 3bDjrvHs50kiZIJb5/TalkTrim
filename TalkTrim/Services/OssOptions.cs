namespace TalkTrim.Services;

/// <summary>
/// 阿里云 OSS 配置（与 Youtube_Learner 的 oss 段字段对齐）。
/// </summary>
public sealed class OssOptions
{
    public const string SectionName = "Oss";

    /// <summary>为 true 时，音频等文件走 OSS；否则仍用本地上传。</summary>
    public bool Enabled { get; set; }

    /// <summary>Region 或完整 Endpoint，例如 cn-hangzhou 或 oss-ap-northeast-1.aliyuncs.com。</summary>
    public string Region { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string AccessKeySecret { get; set; } = string.Empty;

    /// <summary>对象键前缀，例如 talktrim/。</summary>
    public string Prefix { get; set; } = "talktrim/";

    /// <summary>签名 URL 有效期（秒），默认 2 小时，与 Youtube_Learner 一致。</summary>
    public int SignedUrlExpireSeconds { get; set; } = 2 * 60 * 60;
}
