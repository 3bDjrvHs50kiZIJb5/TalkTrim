using FreeSql.DataAnnotations;
using NeoAdmin.Blazor.Entities;

namespace TalkTrim.Entities.Video;

public enum ProjectJobStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
}

public enum ProjectJobType
{
    Transcribe = 1,
    Encode = 2,
}

/// <summary>
/// 视频项目后台任务（口播稿识别、成片压制）。
/// </summary>
[Table(Name = "video_project_job")]
public class ProjectJob : EntityCreated
{
    public long ProjectId { get; set; }

    public ProjectJobType JobType { get; set; }

    public ProjectJobStatus Status { get; set; }

    [Column(StringLength = 500)]
    public string ProgressMessage { get; set; } = string.Empty;

    public int ProgressPercent { get; set; }

    [Column(StringLength = 2000)]
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// 提交任务时的站点根地址，用于后台解析 /uploads 等媒体路径。
    /// </summary>
    [Column(StringLength = 500)]
    public string SiteBaseUri { get; set; } = string.Empty;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}
