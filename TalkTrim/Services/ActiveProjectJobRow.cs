using TalkTrim.Entities.Video;

namespace TalkTrim.Services;

/// <summary>
/// 进行中的后台任务及其所属项目信息（列表展示用）。
/// </summary>
public sealed class ActiveProjectJobRow
{
    public required ProjectJob Job { get; init; }

    public string ProjectName { get; init; } = string.Empty;

    public string ProjectCode { get; init; } = string.Empty;
}
