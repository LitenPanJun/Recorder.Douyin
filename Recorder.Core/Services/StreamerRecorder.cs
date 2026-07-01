using System.Diagnostics;
using API.Douyin;
using API.Douyin.Models;
using Downloader.Douyin;
using Downloader.Douyin.Models;
using Downloader.Douyin.Services;
using DouyinDanmaku.Models;
using DouyinDanmaku.Services;
using Recorder.Core.Models;

namespace Recorder.Core.Services;

public class StreamerRecorder
{
    private readonly StreamerConfig _config;
    private readonly DefaultConfig _defaults;
    private readonly DouyinLiveClient _liveClient;
    private readonly StreamDownloader _downloader;
    private readonly CancellationTokenSource _cts;
    private volatile bool _stopRequested;

    private string BaseDir => _config.OutputDir ?? _defaults.OutputDir;
    private string QualityName => _config.Quality ?? _defaults.Quality;
    private bool EnableHevc => _config.EnableHevc ?? _defaults.EnableHevc;
    private int Crf => _config.Crf ?? _defaults.Crf;
    private TimeSpan SegmentDuration =>
        TimeSpan.FromMinutes(Math.Max(_config.SegmentDuration ?? _defaults.SegmentDuration, 0.1));

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(60);

    public StreamerStatus Status { get; } = new()
    {
        Id = string.Empty,
        Name = string.Empty,
        State = "等待中"
    };

    public string StreamerId => _config.Id;
    public CancellationToken Token => _cts.Token;

    public event Action<StreamerStatus>? StatusChanged;

    public StreamerRecorder(StreamerConfig config, DefaultConfig defaults, DouyinLiveClient liveClient)
    {
        _config = config;
        _defaults = defaults;
        _liveClient = liveClient;
        _downloader = new StreamDownloader();
        _cts = new CancellationTokenSource();

        Status.Id = config.Id;
        Status.Name = !string.IsNullOrEmpty(config.Name) ? config.Name : config.RoomId;
    }

    public void Stop()
    {
        _stopRequested = true;
        _downloader.Cancel();
        // 不取消 _cts.Token — 让已入队的编码和合并任务继续完成
    }

    public async Task RunAsync()
    {
        while (!_cts.Token.IsCancellationRequested && !_stopRequested)
        {
            LiveRoomDetail? detail = null;
            SetStatus("解析中");

            try
            {
                detail = await ResolveRoomAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus("错误", ex.Message);
                await DelaySafe(RetryDelay);
                continue;
            }

            if (detail?.IsLive != true)
            {
                SetStatus("等待中", "未开播");
                await DelaySafe(RetryDelay);
                continue;
            }

            SetStatus("录制中", detail.Title);

            try
            {
                await RecordStreamAsync(detail);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus("错误", ex.Message);
            }

            await DelaySafe(TimeSpan.FromSeconds(10));
        }

        SetStatus("已停止");
    }

    private async Task RecordStreamAsync(LiveRoomDetail detail)
    {
        List<PlayQuality> qualities;
        try
        {
            qualities = await _liveClient.GetPlayQualitiesAsync(detail);
        }
        catch (Exception ex)
        {
            SetStatus("错误", $"获取画质失败: {ex.Message}");
            return;
        }

        var quality = SelectQuality(qualities);
        if (quality == null)
        {
            SetStatus("错误", "无匹配画质");
            return;
        }

        var streamUrl = quality.Urls.FirstOrDefault();
        if (string.IsNullOrEmpty(streamUrl))
        {
            SetStatus("错误", "无推流地址");
            return;
        }

        var now = DateTime.Now;
        var datePart = $"{now:yyyy-MM-dd}_{now:HH-mm-ss}";
        var safeTitle = SanitizeFileName(detail.Title);
        var dirName = !string.IsNullOrEmpty(_config.Name)
            ? SanitizeFileName(_config.Name)
            : SanitizeFileName(detail.UserName);
        var outputDir = Path.Combine(BaseDir, dirName);
        Directory.CreateDirectory(outputDir);
        var outputBasePath = Path.Combine(outputDir, $"{datePart}_{safeTitle}");

        var recordingStopwatch = Stopwatch.StartNew();
        var danmakuCount = 0;

        // 弹幕录制
        var danmakuClient = new DouyinDanmakuClient();
        var danmakuPath = $"{outputBasePath}_Danmaku.txt";

        danmakuClient.OnMessage += msg =>
        {
            Interlocked.Increment(ref danmakuCount);
            var elapsed = recordingStopwatch.Elapsed;
            var tc = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            var line = $"[{tc}] [{msg.Type}] {msg.UserName}: {msg.Content}";
            try { File.AppendAllText(danmakuPath, line + "\n"); }
            catch { }
        };

        danmakuClient.OnReady += () =>
        {
            try
            {
                var elapsed = recordingStopwatch.Elapsed;
                var tc = $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                File.AppendAllText(danmakuPath, $"[{tc}] [系统] 弹幕连接已建立\n");
            }
            catch { }
        };

        if (detail.DanmakuData != null)
        {
            var danmakuArgs = new DouyinDanmakuArgs(
                detail.DanmakuData.WebRid,
                detail.DanmakuData.RoomId,
                detail.DanmakuData.UserId,
                detail.DanmakuData.Cookie);

            _ = danmakuClient.StartAsync(danmakuArgs).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    Console.Error.WriteLine($"\n[弹幕错误] {t.Exception?.InnerException?.Message}");
            });
        }

        // 下载
        var progressObj = new Progress<DownloadProgress>(p =>
        {
            Status.BytesDownloaded = p.BytesDownloaded;
            Status.SpeedFormatted = p.SpeedFormatted;
            Status.Elapsed = p.Elapsed;

            if (!string.IsNullOrEmpty(p.CurrentSegment) && p.CurrentSegment.Contains("HEVC"))
                SetStatus("编码中", p.CurrentSegment);
            else if (!string.IsNullOrEmpty(p.CurrentSegment))
                SetStatus("录制中", p.CurrentSegment);

            StatusChanged?.Invoke(Status);
        });

        DownloadResult? result;
        try
        {
            SetStatus("录制中", quality.Name);
            result = await _downloader.DownloadAsync(
                streamUrl, outputBasePath,
                SegmentDuration, EnableHevc, Crf,
                progressObj, _cts.Token);
        }
        finally
        {
            await danmakuClient.StopAsync();
        }

        if (result == null || result.SegmentFiles.Count == 0)
        {
            SetStatus("等待中", "未获取到分段");
            return;
        }

        // 合并
        SetStatus("合并中");
        var ext = EnableHevc ? ".mkv" : ".flv";
        var mergedPath = $"{outputBasePath}{ext}";

        try
        {
            if (result.SegmentFiles.Count > 1)
            {
                await SegmentMerger.MergeAsync(
                    result.SegmentFiles, mergedPath, ct: _cts.Token);

                foreach (var seg in result.SegmentFiles)
                {
                    try { File.Delete(seg); } catch { }
                }
            }
            else
            {
                File.Move(result.SegmentFiles[0], mergedPath);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            SetStatus("部分完成", $"合并失败: {ex.Message}");
            return;
        }

        SetStatus("录制完成", mergedPath);
    }

    private PlayQuality? SelectQuality(List<PlayQuality> qualities)
    {
        var preferred = qualities.FirstOrDefault(q =>
            q.Name.Contains(QualityName, StringComparison.OrdinalIgnoreCase));

        return preferred ?? qualities.FirstOrDefault();
    }

    private async Task<LiveRoomDetail?> ResolveRoomAsync(CancellationToken ct)
    {
        var roomInput = _config.RoomId;
        if (string.IsNullOrEmpty(roomInput)) return null;

        ct.ThrowIfCancellationRequested();

        if (roomInput.All(char.IsDigit) && roomInput.Length > 16)
            return await _liveClient.GetRoomDetailAsync(roomInput);

        return await _liveClient.GetRoomDetailByUserUniqueIdAsync(roomInput);
    }

    private async Task DelaySafe(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private void SetStatus(string state, string detail = "")
    {
        Status.State = state;
        Status.Detail = detail;
        StatusChanged?.Invoke(Status);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}
