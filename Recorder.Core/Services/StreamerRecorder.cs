using System.Diagnostics;
using API.Douyin;
using API.Douyin.Models;
using Downloader.Douyin;
using Downloader.Douyin.Models;
using Downloader.Douyin.Services;
using DouyinDanmaku.Models;
using DouyinDanmaku.Services;
using Recorder.Core.Models;
using Recorder.Shared;

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

    private volatile bool _isRecording;
    private Task? _recordingTask;

    public bool IsRecording => _isRecording;
    public Task? RecordingTask => _recordingTask;

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
        Status.Name = !string.IsNullOrEmpty(config.Name) ? config.Name : config.UniqueId;
    }

    public void Stop()
    {
        _stopRequested = true;
        _downloader.Cancel();
        // 不取消 _cts.Token — 让已入队的编码和合并任务继续完成
    }

    public async Task<LiveRoomDetail?> PollAsync(CancellationToken ct)
    {
        if (_stopRequested) return null;
        SetStatus("解析中");
        try
        {
            var detail = await ResolveRoomAsync(ct);
            if (detail?.IsLive == true)
            {
                SetStatus("等待中", $"开播: {detail.Title}");
                return detail;
            }
            SetStatus("等待中", "未开播");
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch (CaptchaRequiredException cap)
        {
            Log.Warn($"[验证码] {cap.Url} 需要人工验证");
            try { Process.Start(new ProcessStartInfo(cap.Url) { UseShellExecute = true }); } catch { }
            Console.WriteLine();
            Console.WriteLine("═══════════════════════════════════════════");
            Console.WriteLine("  需要手动完成验证码:");
            Console.WriteLine($"  浏览器已打开: {cap.Url}");
            Console.WriteLine("  完成后按 F12 → Console → 输入:");
            Console.WriteLine("    copy(document.cookie)");
            Console.WriteLine("  然后粘贴到这里，按回车:");
            Console.Write("  cookie> ");
            var pasted = Console.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(pasted))
            {
                _liveClient.SetCookie(pasted);
                Log.Info("[验证码] cookie 已更新，重新请求...");
                try { return await ResolveRoomAsync(ct); }
                catch (CaptchaRequiredException) { }
                catch (Exception ex) { Log.Error(ex); }
            }
            SetStatus("等待中", "验证码未通过");
            return null;
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (msg.Contains("搜索无结果") || msg.Contains("当前未开播") || msg.Contains("未找到"))
                SetStatus("等待中", "未开播");
            else
            {
                Log.Error(ex);
                SetStatus("错误", msg);
            }
            return null;
        }
    }

    public void StartRecording(LiveRoomDetail detail)
    {
        if (_stopRequested || _isRecording) return;
        _isRecording = true;
        _recordingTask = RecordAndFinishAsync(detail);
    }

    public async Task WaitForCompletionAsync()
    {
        if (_recordingTask != null)
        {
            try { await _recordingTask; }
            catch { }
        }
    }

    private async Task RecordAndFinishAsync(LiveRoomDetail detail)
    {
        try
        {
            SetStatus("录制中", detail.Title);
            await RecordStreamAsync(detail);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SetStatus("错误", ex.Message);
        }
        finally
        {
            _isRecording = false;
        }
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
            var extra = "";
            if (msg.Type == LiveMessageType.Gift && msg.Data is GiftInfo gift)
                extra = $" (💎{gift.DiamondCount} 🔁{gift.ComboCount}) [{gift.Describe}] giftId={gift.GiftId} to={gift.ToUserName}";
            var line = $"[{tc}] [{msg.Type}] {msg.UserName}: {msg.Content}{extra}";
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
                    Log.Error($"[弹幕错误] {t.Exception?.InnerException?.Message}");
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
        // 有 roomId → 直播状态接口判定 + 房间API获取全量数据
        if (!string.IsNullOrEmpty(_config.RoomId))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var status = await _liveClient.GetLiveStatusAsync(_config.RoomId);
                if (status.IsLive)
                    return await _liveClient.GetRoomDetailAsync(_config.RoomId);
            }
            catch { }

            return await _liveClient.GetRoomDetailAsync(_config.RoomId);
        }

        // 无 roomId → 走抖音号解析流程
        if (string.IsNullOrEmpty(_config.UniqueId)) return null;
        ct.ThrowIfCancellationRequested();
        return await _liveClient.GetRoomDetailByUserUniqueIdAsync(_config.UniqueId);
    }

    public void SetRoomId(string roomId)
    {
        if (string.IsNullOrEmpty(roomId) || roomId.Length <= 16 || !roomId.All(char.IsDigit))
            return;
        _config.RoomId = roomId;
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
