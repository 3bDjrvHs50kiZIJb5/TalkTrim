using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TalkTrim.Models.Subtitle;
using TalkTrim.Services;

namespace TalkTrim.Services.DashScope;

/// <summary>
/// 使用 qwen-turbo 批量翻译字幕（参考 Youtube_Learner subtitle.ts translateCues）。
/// </summary>
public sealed partial class SubtitleTranslationService
{
    private const string ChatUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
    private const int BatchSize = 40;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly DashScopeOptions _options;

    public SubtitleTranslationService(HttpClient httpClient, IOptions<DashScopeOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<List<SubtitleCue>> TranslateCuesAsync(
        IReadOnlyList<SubtitleCue> cues,
        IProgress<ProjectJobProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (cues.Count == 0)
        {
            return [];
        }

        var apiKey = _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("未配置 DashScope:ApiKey，无法翻译字幕。");
        }

        var target = _options.IsChineseAsrPrimary
            ? "英文"
            : (string.IsNullOrWhiteSpace(_options.TranslateTarget) ? "中文" : _options.TranslateTarget);
        var sourceLabel = _options.IsChineseAsrPrimary ? "中文字幕" : "英文字幕";
        var result = cues.Select(c => new SubtitleCue
        {
            Id = c.Id,
            StartMs = c.StartMs,
            EndMs = c.EndMs,
            Text = c.Text,
            Words = c.Words,
        }).ToList();

        var batches = new List<(int Start, List<SubtitleCue> Items)>();
        for (var i = 0; i < cues.Count; i += BatchSize)
        {
            var slice = cues.Skip(i).Take(BatchSize).ToList();
            batches.Add((i, slice));
        }

        var concurrency = Math.Max(1, _options.TranslateConcurrency);
        var completedBatches = 0;
        var totalBatches = batches.Count;
        await Parallel.ForEachAsync(
            batches,
            new ParallelOptions { MaxDegreeOfParallelism = concurrency, CancellationToken = cancellationToken },
            async (batch, ct) =>
            {
                var prompt =
                    $"请把下列{sourceLabel}翻译成{target}，保持句子编号对应，语气口语化，只输出\"编号: 译文\"每行一条，不要多余说明。\n\n" +
                    string.Join('\n', batch.Items.Select((c, i) => $"{i + 1}: {c.Text}"));

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, ChatUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    request.Content = JsonContent.Create(new
                    {
                        model = "qwen-turbo",
                        messages = new object[]
                        {
                            new { role = "system", content = "你是一个专业的字幕翻译助手。" },
                            new { role = "user", content = prompt },
                        },
                        temperature = 0.3,
                    });

                    using var response = await _httpClient.SendAsync(request, ct);
                    response.EnsureSuccessStatusCode();
                    var body = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, ct);
                    var content = body?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
                    ApplyTranslationLines(result, batch.Start, content);
                }
                catch
                {
                    // 单批失败不阻断其余批次（与 Youtube_Learner 一致）
                }
                finally
                {
                    var done = Interlocked.Increment(ref completedBatches);
                    if (progress is not null && totalBatches > 0)
                    {
                        var localPercent = (int)Math.Clamp(done * 100.0 / totalBatches, 0, 100);
                        var overall = 72 + (int)Math.Round(localPercent * 0.25);
                        progress.Report(new ProjectJobProgressReport(
                            $"正在翻译字幕… {done}/{totalBatches} 批",
                            overall));
                    }
                }
            });

        return result;
    }

    private static void ApplyTranslationLines(List<SubtitleCue> result, int batchStart, string content)
    {
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = TranslationLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var idx = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - 1;
            var globalIndex = batchStart + idx;
            if (globalIndex < 0 || globalIndex >= result.Count)
            {
                continue;
            }

            result[globalIndex].Translation = SubtitleTextNormalizer.Normalize(match.Groups[2].Value);
        }
    }

    [GeneratedRegex(@"^(\d+)[：:.\s]+(.*)$")]
    private static partial Regex TranslationLineRegex();

    private sealed class ChatResponse
    {
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        public ChatMessage? Message { get; set; }
    }

    private sealed class ChatMessage
    {
        public string? Content { get; set; }
    }
}
