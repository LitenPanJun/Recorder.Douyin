using System.Collections.Concurrent;
using API.Douyin;
using Downloader.Douyin;
using Downloader.Douyin.Services;
using Recorder.Core.Models;
using Recorder.Core.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

try { Console.CursorVisible = true; } catch { }

#region CLI 参数

var configPath = "./config.json";
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "--config" && i + 1 < args.Length)
        configPath = args[i + 1];
    else if (args[i] is "--help" or "-h")
    {
        Console.WriteLine("抖音录播姬 - 多主播并行录制工具");
        Console.WriteLine();
        Console.WriteLine("用法: Recorder.Douyin [--config <配置路径>]");
        Console.WriteLine();
        Console.WriteLine("配置为 JSON 格式，修改后自动热更新。");
        Console.WriteLine("首次运行会自动生成示例配置文件。");
        return;
    }
}

#endregion

#region 配置

var configWatcher = new ConfigWatcher(configPath);
var config = configWatcher.GetConfig();

Console.WriteLine("=== 抖音录播姬 (多主播并行录制) ===");
Console.WriteLine($"配置: {Path.GetFullPath(configPath)}\n");

if (config.Streamers.Count == 0)
{
    Console.WriteLine("配置文件中尚无主播。");
    Console.WriteLine("编辑配置文件添加主播后，程序将自动开始录制。\n");
}

#endregion

#region 初始化

var liveClient = new DouyinLiveClient();
var activeTasks = new ConcurrentDictionary<string, StreamerRecorder>();
var cts = new CancellationTokenSource();
var firstCancel = true;
var statuses = new ConcurrentDictionary<string, StreamerStatus>();

if (!HevcEncodingService.IsAvailable)
    Console.Error.WriteLine($"[警告] 未找到 ffmpeg，HEVC 编码不可用 ({HevcEncodingService.NotAvailableReason})");

#endregion

#region 信号处理

Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    if (cts.IsCancellationRequested)
    {
        Console.Error.WriteLine("\n[强制退出]");
        Environment.Exit(1);
    }
    if (firstCancel)
    {
        firstCancel = false;
        Console.Error.WriteLine("\n[停止] 正在停止所有录制任务...");
    }
    cts.Cancel();
};

#endregion

#region 状态渲染 (每 2 秒固定行刷新)

var statusOriginRow = -1;
var prevTotalLines = 0;

void RenderStatus()
{
    var entries = statuses.Values.ToArray();
    if (entries.Length == 0) return;

    var totalLines = entries.Length * 2;
    var width = Console.WindowWidth - 1;
    if (width < 20) width = 79;

    if (statusOriginRow < 0)
        statusOriginRow = Console.CursorTop;

    if (Console.CursorTop > statusOriginRow + totalLines)
        statusOriginRow = Console.CursorTop - totalLines;

    statusOriginRow = Math.Clamp(statusOriginRow, 0, Math.Max(0, Console.BufferHeight - totalLines));

    Console.SetCursorPosition(0, statusOriginRow);

    var now = DateTime.Now;
    var ts = $"[{now:HH:mm:ss}]";

    foreach (var s in entries)
    {
        var detail = !string.IsNullOrEmpty(s.Detail) ? $" ({s.Detail})" : "";
        var size = s.BytesDownloaded > 0 ? $" {FormatSize(s.BytesDownloaded)}" : "";
        var speed = !string.IsNullOrEmpty(s.SpeedFormatted) ? $" @ {s.SpeedFormatted}" : "";
        var line1 = $"{ts} {s.Name}: {s.State}{detail}{size}{speed}";
        if (line1.Length > width) line1 = line1[..width];
        Console.Write(line1.PadRight(width) + "\n");

        var isEncoding = s.State == "编码中" && !string.IsNullOrEmpty(s.Detail);
        var line2 = isEncoding ? $"{ts} {s.Name} 编码: {s.Detail}" : "";
        if (line2.Length > width) line2 = line2[..width];
        Console.Write(line2.PadRight(width) + "\n");
    }

    // 主播减少时擦除旧行残影
    for (var i = totalLines; i < prevTotalLines; i++)
        Console.Write("".PadRight(width) + "\n");

    prevTotalLines = totalLines;
}

var statusTimer = new Timer(_ => { try { RenderStatus(); } catch { } }, null, 2000, 2000);

#endregion

#region 多主播管理

void StartStreamer(StreamerConfig sc)
{
    if (activeTasks.ContainsKey(sc.Id))
        return;

    var recorder = new StreamerRecorder(sc, config.Defaults, liveClient);
    recorder.StatusChanged += status =>
    {
        statuses[status.Id] = status;
    };
    activeTasks[sc.Id] = recorder;
    _ = Task.Run(() => recorder.RunAsync(), CancellationToken.None);
}

void StopStreamer(string id)
{
    if (activeTasks.TryRemove(id, out var recorder))
    {
        recorder.Stop();
    }
    statuses.TryRemove(id, out _);
}

void SyncStreamers(AppConfig cfg)
{
    var newIds = new HashSet<string>(cfg.Streamers.Select(s => s.Id));
    var oldIds = new HashSet<string>(activeTasks.Keys);

    foreach (var id in oldIds.Except(newIds))
        StopStreamer(id);

    foreach (var sc in cfg.Streamers)
        StartStreamer(sc);
}

configWatcher.ConfigChanged += cfg =>
{
    config = cfg;
    SyncStreamers(cfg);
};

// 初始启动
SyncStreamers(config);

#endregion

#region 主等待

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) { }

#endregion

#region 关闭清理

statusTimer.Dispose();
configWatcher.Dispose();

Console.WriteLine("\n正在等待录制任务结束...");

foreach (var r in activeTasks.Values)
    r.Stop();

if (activeTasks.Count > 0)
{
    Console.Write("等待录制任务结束...");
    await Task.Delay(5000);
    Console.WriteLine("完成");
}

Console.WriteLine("再见!");

#endregion

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
