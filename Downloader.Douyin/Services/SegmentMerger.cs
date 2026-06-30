using System.Diagnostics;
using System.Text;

namespace Downloader.Douyin.Services;

public static class SegmentMerger
{
    private static string? FfmpegPath => HevcEncodingService.FfmpegExecutablePath;

    public static bool CanMerge => FfmpegPath != null;

    public static async Task<string> MergeAsync(
        List<string> segmentFiles,
        string outputPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (segmentFiles.Count == 0)
            throw new ArgumentException("No segments to merge");

        if (segmentFiles.Count == 1)
        {
            File.Move(segmentFiles[0], outputPath);
            return outputPath;
        }

        var ext = Path.GetExtension(segmentFiles[0]).ToLowerInvariant();

        if (FfmpegPath != null)
            return await MergeWithFfmpegAsync(segmentFiles, outputPath, ext, progress, ct);

        if (ext == ".flv")
            return MergeFlvBinary(segmentFiles, outputPath);

        throw new InvalidOperationException(
            $"ffmpeg is required to merge {ext} files. Please install ffmpeg and ensure it's in PATH.");
    }

    private static async Task<string> MergeWithFfmpegAsync(
        List<string> segmentFiles,
        string outputPath,
        string ext,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var concatFile = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? ".",
            $".concat_{Path.GetFileNameWithoutExtension(outputPath)}.txt");

        try
        {
            var lines = segmentFiles.Select(f => $"file '{f.Replace("'", "'\\''")}'");
            await File.WriteAllTextAsync(concatFile, string.Join("\n", lines), Encoding.UTF8, ct);

            var args = $"-f concat -safe 0 -i \"{concatFile}\" -c copy -y \"{outputPath}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = FfmpegPath!,
                Arguments = args,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var durationRegex = new System.Text.RegularExpressions.Regex(
                @"Duration:\s*(\d+):(\d+):(\d+\.\d+)");
            var timeRegex = new System.Text.RegularExpressions.Regex(
                @"time=(\d+):(\d+):(\d+\.\d+)");
            var totalDuration = TimeSpan.Zero;

            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct);
                if (line == null) break;

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
                if (tm.Success && totalDuration > TimeSpan.Zero)
                {
                    var elapsed = new TimeSpan(
                        0,
                        int.Parse(tm.Groups[1].Value),
                        int.Parse(tm.Groups[2].Value),
                        0,
                        (int)(double.Parse(tm.Groups[3].Value) * 1000));
                    progress?.Report(Math.Clamp(
                        elapsed.TotalMilliseconds / totalDuration.TotalMilliseconds * 100,
                        0, 99.9));
                }
            }

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
                throw new Exception($"ffmpeg merge failed (exit code {process.ExitCode})");

            progress?.Report(100);
        }
        finally
        {
            try { File.Delete(concatFile); } catch { }
        }

        return outputPath;
    }

    private static string MergeFlvBinary(List<string> segmentFiles, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath) ?? ".";
        Directory.CreateDirectory(dir);

        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write);

        for (var i = 0; i < segmentFiles.Count; i++)
        {
            var segmentBytes = File.ReadAllBytes(segmentFiles[i]);

            if (i == 0)
            {
                output.Write(segmentBytes, 0, segmentBytes.Length);
            }
            else
            {
                var offset = FindFlvBodyStart(segmentBytes);
                output.Write(segmentBytes, offset, segmentBytes.Length - offset);
            }
        }

        return outputPath;
    }

    private static int FindFlvBodyStart(byte[] data)
    {
        if (data.Length < 13) return 0;
        if (data[0] != 'F' || data[1] != 'L' || data[2] != 'V') return 0;

        var headerLen = (data[5] << 24) | (data[6] << 16) | (data[7] << 8) | data[8];
        return headerLen + 4;
    }

}
