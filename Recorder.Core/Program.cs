using API.Douyin;
using API.Douyin.Models;
using Downloader.Douyin;
using Downloader.Douyin.Models;
using Downloader.Douyin.Services;
using DouyinDanmaku.Models;
using DouyinDanmaku.Services;
using Recorder.Shared;

Console.OutputEncoding = System.Text.Encoding.UTF8;

Console.WriteLine("=== 抖音录播姬 ===\n");

#region 参数交互

Console.Write("请输入直播间 ID 或用户唯一标识: ");
var roomInput = Console.ReadLine()?.Trim();
if (string.IsNullOrEmpty(roomInput)) return;

Console.Write("是否启用 HEVC 编码? (y/n, 默认 n): ");
var hevcInput = Console.ReadLine()?.Trim().ToLower();
var enableHevc = hevcInput is "y" or "yes";

var crf = 24;
if (enableHevc)
{
    Console.Write("请输入 CRF 值 (0-51, 默认 24): ");
    var crfInput = Console.ReadLine()?.Trim();
    if (!string.IsNullOrEmpty(crfInput))
        int.TryParse(crfInput, out crf);
    crf = Math.Clamp(crf, 0, 51);
}

Console.Write("请输入分段时长(分钟, 默认 10): ");
var segInput = Console.ReadLine()?.Trim();
var segmentDuration = TimeSpan.FromMinutes(
    !string.IsNullOrEmpty(segInput) && int.TryParse(segInput, out var segMin) ? segMin : 10);

Console.Write("请输入保存目录 (默认 ./recordings): ");
var baseDir = Console.ReadLine()?.Trim();
if (string.IsNullOrEmpty(baseDir))
    baseDir = "./recordings";

Console.WriteLine();

#endregion

#region 解析直播间

Console.Write("正在解析直播间信息... ");

var liveClient = new DouyinLiveClient();
LiveRoomDetail detail;

try
{
    detail = roomInput.All(char.IsDigit) && roomInput.Length > 16
        ? await liveClient.GetRoomDetailAsync(roomInput)
        : await liveClient.GetRoomDetailByUserUniqueIdAsync(roomInput);
}
catch (Exception ex)
{
    Console.WriteLine($"\n[错误] 解析失败: {ex.Message}");
    return;
}

if (!detail.IsLive)
{
    Console.WriteLine($"\n[错误] 当前未开播");
    return;
}

Console.WriteLine("完成\n");
Console.WriteLine($"主播: {detail.UserName}");
Console.WriteLine($"标题: {detail.Title}");
Console.WriteLine($"状态: 直播中 (在线 {detail.Online} 人)\n");

#endregion

#region 画质选择

var qualities = await liveClient.GetPlayQualitiesAsync(detail);
if (qualities.Count == 0)
{
    Console.WriteLine("[错误] 无法获取可用画质");
    return;
}

Console.WriteLine("可用画质:");
for (var i = 0; i < qualities.Count; i++)
{
    var q = qualities[i];
    Console.WriteLine($"  {i + 1}. {q.Name} ({q.StreamType})");
}

Console.Write($"\n请选择画质 (1-{qualities.Count}, 默认 1): ");
var qInput = Console.ReadLine()?.Trim();
var qIndex = (!string.IsNullOrEmpty(qInput) && int.TryParse(qInput, out var qi) ? qi : 1) - 1;
qIndex = Math.Clamp(qIndex, 0, qualities.Count - 1);
var selectedQuality = qualities[qIndex];

Console.WriteLine();

#endregion

#region 文件命名

var now = DateTime.Now;
var datePart = $"直播{now:yyyy-MM-dd}_{now:HH-mm-ss}";
var safeTitle = SanitizeFileName(detail.Title);
var safeUserName = SanitizeFileName(detail.UserName);
var baseName = $"{datePart}_{safeTitle}";
var outputDir = Path.Combine(baseDir, safeUserName);
Directory.CreateDirectory(outputDir);
var outputBasePath = Path.Combine(outputDir, baseName);

#endregion

#region 录制

Console.WriteLine("正在开始录制...\n");

var danmakuCount = 0;
var downloadCompleted = false;
var cancelCts = new CancellationTokenSource();

// 弹幕接收
var danmakuClient = new DouyinDanmakuClient();
var danmakuPath = $"{outputBasePath}_弹幕.txt";

danmakuClient.OnMessage += msg =>
{
    Interlocked.Increment(ref danmakuCount);
    var line = $"[{msg.Type}] {msg.UserName}: {msg.Content}";
    try
    {
        File.AppendAllText(danmakuPath, line + "\n");
    }
    catch { }
};

danmakuClient.OnReady += () =>
{
    try
    {
        File.AppendAllText(danmakuPath, $"[系统] 弹幕连接已建立 ({now:yyyy-MM-dd HH:mm:ss})\n");
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

// 推流下载
var downloader = new StreamDownloader();
var streamUrl = selectedQuality.Urls.FirstOrDefault() ?? "";
if (string.IsNullOrEmpty(streamUrl))
{
    Console.WriteLine("[错误] 无可用的推流地址");
    return;
}

var progressObj = new Progress<DownloadProgress>(p =>
{
    if (!downloadCompleted && !cancelCts.Token.IsCancellationRequested)
    {
        var total = p.TotalBytes.HasValue
            ? $"{FormatSize(p.TotalBytes.Value),8}"
            : "    未知";
        Console.Write($"\r推流: {FormatSize(p.BytesDownloaded),8} / {total}  速度: {p.SpeedFormatted,10}  弹幕: {danmakuCount,5} 条  {p.CurrentSegment,-20}");
    }
});

DownloadResult? downloadResult = null;

try
{
    downloadResult = await downloader.DownloadAsync(
        streamUrl,
        outputBasePath,
        segmentDuration,
        enableHevc,
        crf,
        progressObj,
        cancelCts.Token);
}
catch (OperationCanceledException)
{
    Console.WriteLine("\n\n[录制] 已取消");
    return;
}
catch (Exception ex)
{
    Console.WriteLine($"\n\n[错误] 推流下载失败: {ex.Message}");
    return;
}
finally
{
    downloadCompleted = true;
    await danmakuClient.StopAsync();
}

Console.WriteLine("\n\n推流下载完成!");

#endregion

#region 分段合并

if (downloadResult.SegmentFiles.Count > 1)
{
    Console.Write("正在合并分段... ");

    var ext = enableHevc ? ".mkv" : ".flv";
    var mergedPath = $"{outputBasePath}{ext}";

    try
    {
        var mergeResult = await SegmentMerger.MergeAsync(
            downloadResult.SegmentFiles, mergedPath,
            ct: CancellationToken.None);

        foreach (var seg in downloadResult.SegmentFiles)
        {
            try { File.Delete(seg); } catch { }
        }

        Console.WriteLine("完成");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n[警告] 分段合并失败: {ex.Message}");
        Console.WriteLine("分段文件已保留在原始目录");
    }
}
else if (downloadResult.SegmentFiles.Count == 1)
{
    var ext = enableHevc ? ".mkv" : ".flv";
    var mergedPath = $"{outputBasePath}{ext}";
    File.Move(downloadResult.SegmentFiles[0], mergedPath);
}

#endregion

#region 录制摘要

Console.WriteLine($"\n=== 录制摘要 ===");
Console.WriteLine($"  主播: {detail.UserName}");
Console.WriteLine($"  标题: {detail.Title}");
Console.WriteLine($"  文件: {outputBasePath}{(enableHevc ? ".mkv" : ".flv")}");
Console.WriteLine($"  大小: {FormatSize(downloadResult.TotalBytes)}");
Console.WriteLine($"  分段: {downloadResult.SegmentCount} 个");
Console.WriteLine($"  弹幕: {danmakuCount} 条");
Console.WriteLine($"  耗时: {(int)downloadResult.Elapsed.TotalHours:D2}:{downloadResult.Elapsed.Minutes:D2}:{downloadResult.Elapsed.Seconds:D2}");
Console.WriteLine($"  HEVC: {(enableHevc ? $"是 (CRF {crf})" : "否")}");

#endregion

static string SanitizeFileName(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
}

static string FormatSize(long bytes)
{
    if (bytes >= 1024L * 1024 * 1024)
        return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
    if (bytes >= 1024 * 1024)
        return $"{bytes / (1024.0 * 1024):F1} MB";
    if (bytes >= 1024)
        return $"{bytes / 1024.0:F1} KB";
    return $"{bytes} B";
}
