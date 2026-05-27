using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TalkTrim.Services;

/// <summary>
/// ffmpeg / ffprobe 调用封装。
/// </summary>
public static partial class FfmpegHelper
{
    [GeneratedRegex(@"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})", RegexOptions.Compiled)]
    private static partial Regex FfmpegTimeRegex();

    public static string ResolveFfmpegPath() => ResolveExecutable("ffmpeg");

    public static string ResolveFfprobePath() => ResolveExecutable("ffprobe");

    public static Task RunAsync(
        string arguments,
        CancellationToken cancellationToken = default) =>
        RunAsync(arguments, totalDurationSec: null, progress: null, cancellationToken);

    public static async Task RunAsync(
        string arguments,
        double? totalDurationSec,
        IProgress<ProjectJobProgressReport>? progress,
        CancellationToken cancellationToken = default)
    {
        var ffmpeg = ResolveFfmpegPath();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = arguments,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stderr = await ReadStderrAsync(process, totalDurationSec, progress, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"ffmpeg 执行失败（exit {process.ExitCode}）：{SummarizeFfmpegError(stderr)}");
        }
    }

    public static async Task<double> GetDurationSecondsAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        var ffprobe = ResolveFfprobePath();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments =
                    $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{inputPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0
            || !double.TryParse(stdout.Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var seconds)
            || seconds <= 0)
        {
            throw new InvalidOperationException("无法读取视频时长，请确认 ffprobe 可用且文件有效。");
        }

        return seconds;
    }

    /// <summary>
    /// 检测媒体文件是否包含至少一条音频流。
    /// </summary>
    public static async Task<bool> HasAudioStreamAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        var ffprobe = ResolveFfprobePath();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffprobe,
                Arguments =
                    $"-v error -select_streams a -show_entries stream=index -of csv=p=0 \"{inputPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout);
    }

    public static string EscapeFilterPath(string filePath)
    {
        return filePath
            .Replace('\\', '/')
            .Replace(":", "\\:")
            .Replace("'", "\\'");
    }

    /// <summary>
    /// 从 ffmpeg stderr 行解析当前处理时刻（秒）。
    /// </summary>
    public static double? TryParseTimeSeconds(string stderrLine)
    {
        var match = FfmpegTimeRegex().Match(stderrLine);
        if (!match.Success)
        {
            return null;
        }

        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var centiseconds = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
        return hours * 3600 + minutes * 60 + seconds + centiseconds / 100.0;
    }

    private static async Task<string> ReadStderrAsync(
        Process process,
        double? totalDurationSec,
        IProgress<ProjectJobProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        var lastReportedPercent = -1;

        while (true)
        {
            var line = await process.StandardError.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            lines.Add(line);

            if (progress is null || totalDurationSec is not > 0)
            {
                continue;
            }

            var currentSec = TryParseTimeSeconds(line);
            if (currentSec is null)
            {
                continue;
            }

            var localPercent = (int)Math.Clamp(currentSec.Value / totalDurationSec.Value * 100, 0, 99);
            if (localPercent <= lastReportedPercent)
            {
                continue;
            }

            lastReportedPercent = localPercent;
            progress.Report(new ProjectJobProgressReport(
                $"ffmpeg 处理中… {localPercent}%",
                localPercent));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string ResolveExecutable(string name)
    {
        foreach (var candidate in new[]
                 {
                     name,
                     $"/opt/homebrew/bin/{name}",
                     $"/usr/local/bin/{name}",
                 })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                process?.WaitForExit(3000);
                if (process is { ExitCode: 0 })
                {
                    return candidate;
                }
            }
            catch
            {
                // try next
            }
        }

        throw new InvalidOperationException(
            $"未找到 {name}，请先安装（macOS: brew install ffmpeg）。");
    }

    private static string SummarizeFfmpegError(string text)
    {
        var lines = text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
        {
            return "未知错误";
        }

        var tail = lines.TakeLast(Math.Min(3, lines.Count));
        return string.Join(" | ", tail);
    }
}
