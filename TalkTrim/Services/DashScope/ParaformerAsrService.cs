using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TalkTrim.Models.Subtitle;
using TalkTrim.Services;

namespace TalkTrim.Services.DashScope;

/// <summary>
/// 阿里云 Paraformer 录音文件识别（参考 Youtube_Learner app/electron/services/asr.ts）。
/// </summary>
public sealed class ParaformerAsrService
{
    private const string SubmitUrl = "https://dashscope.aliyuncs.com/api/v1/services/audio/asr/transcription";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;
    private readonly DashScopeOptions _options;
    private readonly ILogger<ParaformerAsrService> _logger;

    public ParaformerAsrService(
        HttpClient httpClient,
        IOptions<DashScopeOptions> options,
        ILogger<ParaformerAsrService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SubtitleCue>> TranscribeAsync(
        string fileUrl,
        IProgress<ProjectJobProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = GetApiKey();
        var languageHints = _options.LanguageHints is { Length: > 0 }
            ? _options.LanguageHints
            : ["zh"];

        var taskId = await SubmitTaskAsync(apiKey, fileUrl, languageHints, cancellationToken);
        _logger.LogInformation("Paraformer 任务已提交：TaskId={TaskId}", taskId);
        progress?.Report(new ProjectJobProgressReport("语音识别任务已提交，等待云端处理…", 32));

        var transcriptionUrl = await PollTranscriptionUrlAsync(apiKey, taskId, progress, cancellationToken);

        var result = await _httpClient.GetFromJsonAsync<ParaformerResult>(
            transcriptionUrl,
            JsonOptions,
            cancellationToken);

        var sentences = result?.Transcripts?.FirstOrDefault()?.Sentences ?? [];
        var cues = sentences
            .Select((s, index) =>
            {
                var words = MapWords(s.Words);
                var timing = ResolveCueTimingFromWords(words, s.BeginTime, s.EndTime);
                return new SubtitleCue
                {
                    Id = index,
                    StartMs = timing.StartMs,
                    EndMs = timing.EndMs,
                    Text = SubtitleTextNormalizer.Normalize(s.Text),
                    Words = words,
                };
            })
            .Where(c => !string.IsNullOrWhiteSpace(c.Text))
            .ToList();

        if (!_options.SplitLongCuesEnabled || cues.Count == 0)
        {
            return cues;
        }

        var before = cues.Count;
        var split = SubtitleCueSplitService.SplitLongCues(
            cues,
            new SubtitleCueSplitService.SplitOptions
            {
                MaxWords = Math.Max(6, _options.CueSplitMaxWords),
                MaxDurationMs = Math.Max(2000, _options.CueSplitMaxDurationMs),
                FallbackMaxChars = Math.Max(12, _options.CueSplitFallbackMaxChars),
            });

        if (split.Count != before)
        {
            _logger.LogInformation(
                "ASR 长句拆分：{Before} 句 -> {After} 句（MaxWords={MaxWords}, MaxDurationMs={MaxDurationMs}）",
                before,
                split.Count,
                _options.CueSplitMaxWords,
                _options.CueSplitMaxDurationMs);
        }

        return split;
    }

    private string GetApiKey()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("请先在 appsettings 中配置 DashScope:ApiKey（阿里云百炼 API Key）。");
        }

        return _options.ApiKey;
    }

    private async Task<string> SubmitTaskAsync(
        string apiKey,
        string fileUrl,
        string[] languageHints,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SubmitUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("X-DashScope-Async", "enable");
        request.Content = JsonContent.Create(new
        {
            model = "paraformer-v2",
            input = new { file_urls = new[] { fileUrl } },
            parameters = new
            {
                language_hints = languageHints,
                timestamp_alignment_enabled = true,
                disfluency_removal_enabled = _options.DisfluencyRemovalEnabled,
            },
        });

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"ASR 任务提交 HTTP {(int)response.StatusCode}: {json}");
        }

        var taskId = DashScopeTaskJson.GetTaskId(json);
        if (string.IsNullOrWhiteSpace(taskId))
        {
            throw new InvalidOperationException($"ASR 任务提交失败（响应中无 task_id）: {json}");
        }

        return taskId;
    }

    private async Task<string> PollTranscriptionUrlAsync(
        string apiKey,
        string taskId,
        IProgress<ProjectJobProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMinutes(30);
        string? lastStatus = null;
        var pollCount = 0;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://dashscope.aliyuncs.com/api/v1/tasks/{taskId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"ASR 轮询 HTTP {(int)response.StatusCode}: {json}");
            }

            var status = DashScopeTaskJson.GetTaskStatus(json);
            if (string.IsNullOrWhiteSpace(status))
            {
                throw new InvalidOperationException($"ASR 轮询响应缺少 task_status: {json}");
            }

            if (!string.Equals(status, lastStatus, StringComparison.Ordinal))
            {
                _logger.LogInformation("ASR 任务 {TaskId} 状态: {Status}", taskId, status);
                lastStatus = status;
            }

            pollCount++;
            var asrPercent = Math.Min(68, 34 + pollCount);
            progress?.Report(new ProjectJobProgressReport(FormatAsrStatusMessage(status), asrPercent));

            if (IsTaskStatus(status, "SUCCEEDED"))
            {
                var transcriptionUrl = DashScopeTaskJson.GetFirstTranscriptionUrl(json);
                if (string.IsNullOrWhiteSpace(transcriptionUrl))
                {
                    throw new InvalidOperationException(
                        $"ASR 已成功但缺少 transcription_url: {json}");
                }

                return transcriptionUrl;
            }

            if (IsTaskStatus(status, "FAILED"))
            {
                var message = DashScopeTaskJson.GetOutputMessage(json) ?? json;
                throw new InvalidOperationException($"ASR 失败: {message}");
            }

            var subtaskError = DashScopeTaskJson.GetFailedSubtaskMessage(json);
            if (!string.IsNullOrWhiteSpace(subtaskError))
            {
                throw new InvalidOperationException($"ASR 子任务失败: {subtaskError}");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        throw new TimeoutException("ASR 轮询超时（30 分钟）。");
    }

    private static bool IsTaskStatus(string status, string expected) =>
        string.Equals(status.Trim(), expected, StringComparison.OrdinalIgnoreCase);

    private static string FormatAsrStatusMessage(string status)
    {
        if (IsTaskStatus(status, "PENDING"))
        {
            return "语音识别排队中…";
        }

        if (IsTaskStatus(status, "RUNNING"))
        {
            return "语音识别处理中…";
        }

        return $"语音识别状态：{status}";
    }

    private static List<SubtitleWord>? MapWords(List<ParaformerWord>? words)
    {
        if (words is not { Count: > 0 })
        {
            return null;
        }

        var list = new List<SubtitleWord>();
        foreach (var w in words)
        {
            var text = (w.Text ?? w.Word ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text) || w.BeginTime is null || w.EndTime is null)
            {
                continue;
            }

            list.Add(new SubtitleWord
            {
                Text = text,
                StartMs = w.BeginTime.Value,
                EndMs = w.EndTime.Value,
                Punctuation = string.IsNullOrWhiteSpace(w.Punctuation) ? null : w.Punctuation,
            });
        }

        return list.Count > 0 ? list : null;
    }

    private static (int StartMs, int EndMs) ResolveCueTimingFromWords(
        List<SubtitleWord>? words,
        int fallbackStartMs,
        int fallbackEndMs)
    {
        if (words is not { Count: > 0 })
        {
            return (fallbackStartMs, fallbackEndMs);
        }

        var ordered = words
            .Where(w => w.EndMs >= w.StartMs)
            .OrderBy(w => w.StartMs)
            .ToList();
        if (ordered.Count == 0)
        {
            return (fallbackStartMs, fallbackEndMs);
        }

        return (ordered[0].StartMs, ordered.Max(w => w.EndMs));
    }

    private sealed class ParaformerResult
    {
        public List<ParaformerTranscript>? Transcripts { get; set; }
    }

    private sealed class ParaformerTranscript
    {
        public List<ParaformerSentence>? Sentences { get; set; }
    }

    private sealed class ParaformerSentence
    {
        [JsonPropertyName("begin_time")]
        public int BeginTime { get; set; }

        [JsonPropertyName("end_time")]
        public int EndTime { get; set; }

        public string? Text { get; set; }
        public List<ParaformerWord>? Words { get; set; }
    }

    private sealed class ParaformerWord
    {
        [JsonPropertyName("begin_time")]
        public int? BeginTime { get; set; }

        [JsonPropertyName("end_time")]
        public int? EndTime { get; set; }

        public string? Text { get; set; }
        public string? Word { get; set; }
        public string? Punctuation { get; set; }
    }
}
