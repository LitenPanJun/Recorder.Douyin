using System.Diagnostics;
using Downloader.Douyin.Models;
using Downloader.Douyin.Services;
using Recorder.Shared;

namespace Downloader.Douyin;

public class StreamDownloader
{
    private readonly FlvDownloadService _flvDownloader;
    private readonly HevcEncodingService _hevcEncoder = new();
    private readonly SemaphoreSlim _encodeThrottle = new(1, 1);

    public StreamDownloader()
    {
        _flvDownloader = new FlvDownloadService();
    }

    public async Task<DownloadResult> DownloadAsync(
        string url,
        string outputPath,
        TimeSpan? segmentDuration = null,
        bool enableHevc = false,
        int hevcCrf = 24,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        var segDuration = segmentDuration ?? TimeSpan.FromMinutes(10);
        var encodeTasks = new List<Task>();
        var encodeTaskLock = new object();
        var encodeOk = 0;
        var encodeFail = 0;
        var encodeCancel = 0;

        if (enableHevc && !HevcEncodingService.IsAvailable)
        {
            var reason = HevcEncodingService.NotAvailableReason ?? "未知原因";
            Log.Error($"[错误] 无法启用 HEVC 编码: {reason}");
            Log.Warn("[警告] 将使用原始 FLV 格式继续下载");
            enableHevc = false;
        }

        if (enableHevc)
            Log.Info($"[编码] 并发限制: 每次 1 个分段 (SemaphoreSlim)");

        DownloadResult? downloadResult = null;
        try
        {
            downloadResult = await _flvDownloader.DownloadAsync(
                url,
                outputPath,
                segDuration,
                onSegmentCompleted: (index, flvPath, size, cancelToken) =>
                {
                    if (!enableHevc) return Task.CompletedTask;

                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            await _encodeThrottle.WaitAsync(ct);

                            var mkvPath = Path.ChangeExtension(flvPath, ".mkv");

                            progress?.Report(new DownloadProgress
                            {
                                CurrentSegment = $"[{index}] HEVC 编码中...",
                                SegmentsCompleted = index
                            });

                            var fi = new FileInfo(flvPath);
                            if (!fi.Exists || fi.Length < 1024)
                            {
                                Log.Error(
                                    $"[编码错误] 分段 {index}: 源文件过小或不存在 ({fi.Length} B)，跳过");
                                Interlocked.Increment(ref encodeFail);
                                return;
                            }
                            using (var fs = new FileStream(flvPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                var sig = new byte[3];
                                if (await fs.ReadAsync(sig, 0, 3, CancellationToken.None) != 3 ||
                                    sig[0] != 'F' || sig[1] != 'L' || sig[2] != 'V')
                                {
                                    Log.Error(
                                        $"[编码错误] 分段 {index}: 不是合法 FLV 文件，跳过");
                                    Interlocked.Increment(ref encodeFail);
                                    return;
                                }
                            }

                            var sw = Stopwatch.StartNew();
                            await _hevcEncoder.EncodeAsync(
                                flvPath, mkvPath, hevcCrf,
                                ct: CancellationToken.None);
                            sw.Stop();

                            File.Delete(flvPath);

                            progress?.Report(new DownloadProgress
                            {
                                CurrentSegment = $"[{index}] HEVC 完成 ✓",
                                SegmentsCompleted = index
                            });
                            Interlocked.Increment(ref encodeOk);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            Log.Info($"[编码] 分段 {index}: 已取消");
                            Interlocked.Increment(ref encodeCancel);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(
                                $"[编码错误] 分段 {index}: {ex.Message}");
                            progress?.Report(new DownloadProgress
                            {
                                CurrentSegment = $"[{index}] HEVC 失败 ✗",
                                SegmentsCompleted = index
                            });
                            Interlocked.Increment(ref encodeFail);
                        }
                        finally
                        {
                            _encodeThrottle.Release();
                        }
                    });

                    lock (encodeTaskLock) encodeTasks.Add(task);
                    return Task.CompletedTask;
                },
                progress,
                ct);
        }
        catch (OperationCanceledException)
        {
            Log.Info($"[下载] 用户已取消，正在等待 {encodeTasks.Count} 个编码任务完成...");
        }
        catch (Exception ex)
        {
            Log.Error($"[下载错误] {ex.Message}");
        }

        if (enableHevc && encodeTasks.Count > 0)
        {
            progress?.Report(new DownloadProgress
            {
                CurrentSegment = $"等待 {encodeTasks.Count} 个 HEVC 编码任务完成..."
            });
            Log.Info($"[编码] 等待 {encodeTasks.Count} 个编码任务完成...");

            try
            {
                await Task.WhenAll(encodeTasks);
            }
            catch (Exception ex)
            {
                Log.Error($"[编码] 部分任务失败: {ex.Message}");
            }

            Log.Info(
                $"[编码] 汇总: {encodeOk}/{encodeTasks.Count} 成功, {encodeFail} 失败, {encodeCancel} 取消");

            if (encodeFail == 0 && encodeCancel == 0)
                Log.Info("[编码] 所有编码任务处理完毕");
        }

        return new DownloadResult
        {
            TotalBytes = downloadResult?.TotalBytes ?? 0,
            Elapsed = downloadResult?.Elapsed ?? TimeSpan.Zero,
            SegmentCount = downloadResult?.SegmentCount ?? 0,
            OutputDirectory = downloadResult?.OutputDirectory ?? ".",
            BaseFileName = downloadResult?.BaseFileName ?? "",
            HevcEncoded = enableHevc,
            SegmentFiles = downloadResult?.SegmentFiles
                .Select(f => Path.ChangeExtension(f, enableHevc ? ".mkv" : ".flv"))
                .ToList() ?? new List<string>()
        };
    }

    public void Cancel()
    {
        _flvDownloader.Cancel();
    }
}
