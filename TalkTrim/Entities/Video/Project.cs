using FreeSql.DataAnnotations;
using NeoAdmin.Blazor.Entities;

namespace TalkTrim.Entities.Video;

/// <summary>
/// 视频项目
/// </summary>
[Table(Name = "video_project")]
public class Project : EntityModified
{
    [Column(StringLength = 100)]
    public string ProjectName { get; set; } = string.Empty;

    [Column(StringLength = 50)]
    public string ProjectCode { get; set; } = string.Empty;

    [Column(StringLength = 500)]
    public string Remark { get; set; } = string.Empty;

    /// <summary>
    /// 视频简介（发布到平台的文案）
    /// </summary>
    [Column(StringLength = 2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 视频缩略图 URL
    /// </summary>
    [Column(StringLength = 500)]
    public string ThumbnailUrl { get; set; } = string.Empty;

    /// <summary>
    /// 视频片尾 URL（压制完成后拼接到成片末尾，可选）
    /// </summary>
    [Column(StringLength = 500)]
    public string OutroVideoUrl { get; set; } = string.Empty;

    /// <summary>
    /// 关键词列表（逗号分隔存储）
    /// </summary>
    [Column(StringLength = 2000)]
    public string Keywords { get; set; } = string.Empty;

    /// <summary>
    /// 视频文件 URL（OSS 签名地址或本站上传路径，供预览与 ASR 公网访问）
    /// </summary>
    [Column(StringLength = 500)]
    public string VideoFileUrl { get; set; } = string.Empty;

    /// <summary>
    /// 视频素材本站本地链接（如 /uploads/video/xxx.mp4），用于 ffmpeg 提取 WAV，避免从 OSS 再下载。
    /// </summary>
    [Column(StringLength = 500)]
    public string VideoFileLocalUrl { get; set; } = string.Empty;

    /// <summary>
    /// 从视频提取的 16kHz WAV 音频 OSS 地址（供 ASR 等使用）
    /// </summary>
    [Column(StringLength = 500)]
    public string WavUrl { get; set; } = string.Empty;

    /// <summary>
    /// 口播稿内容
    /// </summary>
    [Column(StringLength = -2)]
    public string ScriptContent { get; set; } = string.Empty;

    /// <summary>
    /// 中文字幕
    /// </summary>
    [Column(StringLength = -2)]
    public string ChineseSubtitles { get; set; } = string.Empty;

    /// <summary>
    /// 英文字幕
    /// </summary>
    [Column(StringLength = -2)]
    public string EnglishSubtitles { get; set; } = string.Empty;

    /// <summary>
    /// 中文字幕字体（ASS Fontname，同目录 DouyinSansBold.ttf）
    /// </summary>
    [Column(StringLength = 100)]
    public string SubtitleFontName { get; set; } = "抖音美好体";

    /// <summary>
    /// 中文字幕字号
    /// </summary>
    public int SubtitleFontSize { get; set; } = 44;

    /// <summary>
    /// 中文字幕字体颜色（#RRGGBB）
    /// </summary>
    [Column(StringLength = 20)]
    public string SubtitleFontColor { get; set; } = "#FFFFFF";

    /// <summary>
    /// 中文字幕背景色（#RRGGBB）
    /// </summary>
    [Column(StringLength = 20)]
    public string SubtitleBackgroundColor { get; set; } = "#000000";

    /// <summary>
    /// 英文字幕字体（ASS Fontname，同目录 DouyinSansBold.ttf 的拉丁族名）
    /// </summary>
    [Column(StringLength = 100)]
    public string EnglishSubtitleFontName { get; set; } = "DouyinSans";

    /// <summary>
    /// 英文字幕字号（小于中文，叠在下方）
    /// </summary>
    public int EnglishSubtitleFontSize { get; set; } = 32;

    /// <summary>
    /// 英文字幕字体颜色（#RRGGBB，黄字）
    /// </summary>
    [Column(StringLength = 20)]
    public string EnglishSubtitleFontColor { get; set; } = "#FFD700";

    /// <summary>
    /// 英文字幕背景色（#RRGGBB，深棕底衬托黄字，与中文纯黑底区分）
    /// </summary>
    [Column(StringLength = 20)]
    public string EnglishSubtitleBackgroundColor { get; set; } = "#2A2200";

    /// <summary>
    /// 字幕背景透明度（0～100，100 为完全不透明；中英共用）
    /// </summary>
    public int SubtitleBackgroundOpacity { get; set; } = 75;

    /// <summary>
    /// 视频加速倍数
    /// </summary>
    [Column(Precision = 5, Scale = 2)]
    public decimal SpeedMultiplier { get; set; } = 1.25m;

    /// <summary>
    /// 去口气：句间停顿超过该秒数时裁切至该秒数（0 表示不处理）。
    /// </summary>
    [Column(Precision = 5, Scale = 3)]
    public decimal BreathTrimSeconds { get; set; } = 1m;

    /// <summary>
    /// 成片地址
    /// </summary>
    [Column(StringLength = 500)]
    public string FinishedVideoUrl { get; set; } = string.Empty;

    public bool Status { get; set; } = true;

    /// <summary>
    /// 所属用户（关联 sys_user.Id，创建时写入，不可修改）
    /// </summary>
    [Column(CanUpdate = false)]
    public long UserId { get; set; }

    [Navigate(nameof(UserId))]
    public SysUser? SysUser { get; set; }
}
