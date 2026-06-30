using System.Diagnostics;
using Douyin.StreamDownloader.Models;
using Douyin.StreamDownloader.Services;

namespace Douyin.StreamDownloader;

public class StreamDownloader
{
    private readonly FlvDownloadService _flvDownloader;
    private readonly HevcEncodingService _hevcEncoder;
    private static readonly SemaphoreSlim _encodeThrottle = new(1, 1);

    public StreamDownloader()
    {
        _flvDownloader = new FlvDownloadService();
        _hevcEncoder = new HevcEncodingService();
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

        if (enableHevc && !_hevcEncoder.IsAvailable)
        {
            var reason = HevcEncodingService.NotAvailableReason ?? "未知原因";
            Console.Error.WriteLine($"[错误] 无法启用 HEVC 编码: {reason}");
            Console.Error.WriteLine("[错误] 将使用原始 FLV 格式继续下载");
            enableHevc = false;
        }

        if (enableHevc)
            Console.Error.WriteLine($"[编码] 并发限制: 每次 1 个分段 (SemaphoreSlim)");

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
                        await _encodeThrottle.WaitAsync(CancellationToken.None);
                        try
                        {
                            var mkvPath = Path.ChangeExtension(flvPath, ".mkv");

                            progress?.Report(new DownloadProgress
                            {
                                CurrentSegment = $"[{index}] HEVC 编码中...",
                                SegmentsCompleted = index
                            });
                            Console.Error.WriteLine(
                                $"[编码] 排队中: 分段 {index}");

                            var fi = new FileInfo(flvPath);
                            if (!fi.Exists || fi.Length < 1024)
                            {
                                Console.Error.WriteLine(
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
                                    Console.Error.WriteLine(
                                        $"[编码错误] 分段 {index}: 不是合法 FLV 文件，跳过");
                                    Interlocked.Increment(ref encodeFail);
                                    return;
                                }
                            }

                            var sw = Stopwatch.StartNew();
                            await _hevcEncoder.EncodeAsync(
                                flvPath, mkvPath, hevcCrf,
                                progress: null,
                                ct: CancellationToken.None);
                            sw.Stop();

                            File.Delete(flvPath);

                            Console.Error.WriteLine(
                                $"[编码] 分段 {index}: 完成 ✓ ({sw.Elapsed.TotalMinutes:F1}分)");

                            progress?.Report(new DownloadProgress
                            {
                                CurrentSegment = $"[{index}] HEVC 完成 ✓",
                                SegmentsCompleted = index
                            });
                            Interlocked.Increment(ref encodeOk);
                        }
                        catch (OperationCanceledException)
                        {
                            Console.Error.WriteLine($"[编码] 分段 {index}: 已取消");
                            Interlocked.Increment(ref encodeCancel);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine(
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
            Console.Error.WriteLine("\n[下载] 用户已取消，正在等待 {0} 个编码任务完成...", encodeTasks.Count);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[下载错误] {ex.Message}");
        }

        if (enableHevc && encodeTasks.Count > 0)
        {
            progress?.Report(new DownloadProgress
            {
                CurrentSegment = $"等待 {encodeTasks.Count} 个 HEVC 编码任务完成..."
            });
            Console.Error.WriteLine($"[编码] 等待 {encodeTasks.Count} 个编码任务完成...");

            try
            {
                await Task.WhenAll(encodeTasks);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[编码] 部分任务失败: {ex.Message}");
            }

            Console.Error.WriteLine(
                $"[编码] 汇总: {encodeOk}/{encodeTasks.Count} 成功, {encodeFail} 失败, {encodeCancel} 取消");

            if (encodeFail == 0 && encodeCancel == 0)
                Console.Error.WriteLine("[编码] 所有编码任务处理完毕");
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
