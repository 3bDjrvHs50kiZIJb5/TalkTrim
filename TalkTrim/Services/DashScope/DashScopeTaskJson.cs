using System.Text.Json;

namespace TalkTrim.Services.DashScope;

/// <summary>
/// 用 JsonDocument 读取 DashScope 响应，避免 snake_case 反序列化丢失字段。
/// </summary>
internal static class DashScopeTaskJson
{
    public static string? GetTaskId(string json) =>
        ReadOutputProperty(json, "task_id");

    public static string? GetTaskStatus(string json) =>
        ReadOutputProperty(json, "task_status");

    public static string? GetOutputMessage(string json) =>
        ReadOutputProperty(json, "message") ?? ReadOutputProperty(json, "code");

    public static string? GetFirstTranscriptionUrl(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("output", out var output)
                || !output.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in results.EnumerateArray())
            {
                var url = GetString(item, "transcription_url");
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    /// <summary>主任务未完成但子任务已失败时的错误信息。</summary>
    public static string? GetFailedSubtaskMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("output", out var output)
                || !output.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var item in results.EnumerateArray())
            {
                var subStatus = GetString(item, "subtask_status");
                if (!string.Equals(subStatus, "FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return GetString(item, "message")
                    ?? GetString(item, "code")
                    ?? "子任务失败";
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string? ReadOutputProperty(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("output", out var output))
            {
                return null;
            }

            return GetString(output, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }
}
