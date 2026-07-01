using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Downloader.Douyin.Services;

public class HevcEncodingService
{
    private static readonly string? FfmpegPath;
    private static readonly string? FfmpegNotFoundReason;

    static HevcEncodingService()
    {
        try
        {
            FfmpegPath = FindFfmpeg();
            if (FfmpegPath == null)
                FfmpegNotFoundReason = "ffmpeg not found in PATH or common locations";
        }
        catch (Exception ex)
        {
            FfmpegNotFoundReason = $"ffmpeg detection error: {ex.Message}";
        }
    }

    public static bool IsAvailable => FfmpegPath != null;
    public static string? NotAvailableReason => FfmpegNotFoundReason;
    public static string? FfmpegExecutablePath => FfmpegPath;

    public async Task EncodeAsync(
        string inputFile,
        string? outputFile = null,
        int crf = 24,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (FfmpegPath == null)
            throw new InvalidOperationException(
                FfmpegNotFoundReason ?? "ffmpeg not found");

        if (!File.Exists(inputFile))
            throw new FileNotFoundException($"Input file not found: {inputFile}");

        outputFile ??= Path.ChangeExtension(inputFile, ".mkv");

        var args = $"-i \"{inputFile}\" " +
                   $"-map 0:v:0? -map 0:a:0? " +
                   $"-c:v libx265 " +
                   $"-crf {crf} " +
                   $"-preset medium " +
                   $"-tag:v hvc1 " +
                   $"-c:a copy " +
                   $"-y \"{outputFile}\"";

        var startInfo = new ProcessStartInfo
        {
            FileName = FfmpegPath,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stderrBuilder = new StringBuilder();
        var totalDuration = TimeSpan.Zero;
        var durationRegex = new Regex(
            @"Duration:\s*(\d+):(\d+):(\d+\.\d+)");
        var timeRegex = new Regex(
            @"time=(\d+):(\d+):(\d+\.\d+)");

        var progressTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct);
                if (line == null) break;

                stderrBuilder.AppendLine(line);

                if (totalDuration == TimeSpan.Zero)
                {
                    var dm = durationRegex.Match(line);
                    if (dm.Success)
                    {
                        totalDuration = new TimeSpan(
                            0,
                            int.Parse(dm.Groups[1].Value),
                            int.Parse(dm.Groups[2].Value),
                            0,
                            (int)(double.Parse(dm.Groups[3].Value) * 1000));
                    }
                }

                var tm = timeRegex.Match(line);
                if (tm.Success)
                {
                    var elapsed = new TimeSpan(
                        0,
                        int.Parse(tm.Groups[1].Value),
                        int.Parse(tm.Groups[2].Value),
                        0,
                        (int)(double.Parse(tm.Groups[3].Value) * 1000));

                    if (totalDuration > TimeSpan.Zero)
                    {
                        var pct = Math.Clamp(
                            elapsed.TotalMilliseconds / totalDuration.TotalMilliseconds * 100,
                            0, 99.9);
                        progress?.Report(pct);
                    }
                }
            }
        }, ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromHours(4));

        using var _ = timeoutCts.Token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch { }
        });

        await process.WaitForExitAsync(timeoutCts.Token);
        await progressTask;

        if (process.ExitCode != 0)
        {
            var stderr = stderrBuilder.ToString();
            Console.Error.WriteLine($"[ffmpeg] Failed (exit code {process.ExitCode}):");
            Console.Error.WriteLine(stderr.Length > 500 ? stderr[..500] + "..." : stderr);
            throw new Exception(
                $"ffmpeg encode failed (exit {process.ExitCode})\n{stderr}");
        }

        progress?.Report(100);
    }

    private static string? FindFfmpeg()
    {
        var searchPaths = new List<string>
        {
            "ffmpeg",
            "ffmpeg.exe",
        };

        var commonDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
            @"C:\ProgramData\chocolatey\bin",
            @"C:\tools\ffmpeg\bin",
            @"C:\ffmpeg\bin",
        };

        foreach (var dir in commonDirs)
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                searchPaths.Add(Path.Combine(dir, "ffmpeg.exe"));
                searchPaths.Add(Path.Combine(dir, "ffmpeg"));
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (pathEnv != null)
        {
            foreach (var p in pathEnv.Split(Path.PathSeparator))
            {
                if (Directory.Exists(p))
                {
                    searchPaths.Add(Path.Combine(p, "ffmpeg.exe"));
                    searchPaths.Add(Path.Combine(p, "ffmpeg"));
                }
            }
        }

        foreach (var candidate in searchPaths.Distinct())
        {
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (proc == null) continue;
                if (!proc.WaitForExit(2000)) { proc.Kill(); continue; }
                if (proc.ExitCode == 0)
                {
                    var firstLine = proc.StandardOutput.ReadLine() ?? "";
                    Console.Error.WriteLine($"[ffmpeg] 已找到: {candidate}");
                    Console.Error.WriteLine($"[ffmpeg] 版本: {firstLine}");
                    return candidate;
                }
            }
            catch { continue; }
        }

        return null;
    }
}
