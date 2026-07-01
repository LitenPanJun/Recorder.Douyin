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

#region 状态渲染 (每 2 秒)

var statusTimer = new Timer(_ =>
{
    try
    {
        var entries = statuses.Values.ToArray();
        if (entries.Length == 0) return;

        var now = DateTime.Now;
        Console.Write($"\r[{now:HH:mm:ss}] ");
        for (var i = 0; i < entries.Length; i++)
        {
            var s = entries[i];
            if (i > 0) Console.Write(" | ");
            var state = s.State;
            var detail = !string.IsNullOrEmpty(s.Detail) ? $" ({s.Detail})" : "";
            var size = s.BytesDownloaded > 0 ? $" {FormatSize(s.BytesDownloaded)}" : "";
            var speed = !string.IsNullOrEmpty(s.SpeedFormatted) ? $" @ {s.SpeedFormatted}" : "";
            Console.Write($"{s.Name}: {state}{detail}{size}{speed}");
        }
        Console.Write(new string(' ', Math.Max(0, Console.WindowWidth - Console.CursorLeft - 1)));
    }
    catch { }
}, null, 2000, 2000);

#endregion

#region 多主播管理

void StartStreamer(StreamerConfig sc)
{
    if (activeTasks.ContainsKey(sc.Id))
    {
        StopStreamer(sc.Id);
    }

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
