using System.Collections.Concurrent;
using System.Text;
using API.Douyin;
using Downloader.Douyin;
using Downloader.Douyin.Services;
using Recorder.Core.Models;
using Recorder.Core.Services;

Console.OutputEncoding = System.Text.Encoding.UTF8;

try { Console.CursorVisible = true; } catch { }

// 错误日志文件 + 全局控制台锁
var errorLogPath = Path.Combine(AppContext.BaseDirectory, "error.log");
var errorLog = new ErrorLogWriter(new StreamWriter(errorLogPath, append: false, Encoding.UTF8));
var consoleLock = errorLog.Lock;
var origOut = Console.Out;
Console.SetOut(new LockedTextWriter(origOut, consoleLock));
Console.SetError(errorLog);

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
var runningTasks = new ConcurrentDictionary<string, Task>();
var cts = new CancellationTokenSource();
var firstCancel = true;
var statuses = new ConcurrentDictionary<string, StreamerStatus>();

if (!HevcEncodingService.IsAvailable)
    errorLog.WriteLine($"[警告] 未找到 ffmpeg，HEVC 编码不可用 ({HevcEncodingService.NotAvailableReason})");

#endregion

#region 信号处理

Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    if (cts.IsCancellationRequested)
    {
        var msg = $"[{DateTime.Now:HH:mm:ss}] [强制退出]\n";
        File.AppendAllText(errorLogPath, msg);
        Environment.Exit(1);
    }
    if (firstCancel)
    {
        firstCancel = false;
        Console.Write($"\r[停止] 正在停止所有录制任务...");
    }
    cts.Cancel();
};

#endregion

#region 状态渲染 (每 2 秒固定行)

var statusOriginRow = -1;
var prevStatusLines = 0;

void RenderStatus()
{
    var entries = statuses.Values.ToArray();
    if (entries.Length == 0) { prevStatusLines = 0; return; }

    var width = Console.WindowWidth - 1;
    if (width < 20) width = 79;
    var hasError = errorLog.ErrorCount > 0;
    var newLineCount = entries.Length + (hasError ? 1 : 0);

    Monitor.Enter(consoleLock);
    try
    {
        if (statusOriginRow < 0)
        {
            statusOriginRow = Console.CursorTop;
        }
        else
        {
            var top = Console.CursorTop;
            if (top != statusOriginRow + prevStatusLines)
                statusOriginRow = Math.Max(0, top - prevStatusLines);
        }

        // 用空白覆盖旧区域（不用 \n，避免滚动导致残影）
        var maxCover = Math.Max(prevStatusLines, newLineCount);
        for (var i = 0; i < maxCover; i++)
        {
            Console.SetCursorPosition(0, statusOriginRow + i);
            Console.Write(new string(' ', width));
        }

        // 写当前状态
        var now = DateTime.Now;
        var ts = $"[{now:HH:mm:ss}]";

        for (var i = 0; i < entries.Length; i++)
        {
            Console.SetCursorPosition(0, statusOriginRow + i);
            var s = entries[i];
            var detail = !string.IsNullOrEmpty(s.Detail) ? $" ({s.Detail})" : "";
            var size = s.BytesDownloaded > 0 ? $" {FormatSize(s.BytesDownloaded)}" : "";
            var speed = !string.IsNullOrEmpty(s.SpeedFormatted) ? $" @ {s.SpeedFormatted}" : "";
            var line = $"{ts} {s.Name}: {s.State}{detail}{size}{speed}";
            if (line.Length > width) line = line[..width];
            Console.Write(line.PadRight(width));
        }

        if (hasError)
        {
            Console.SetCursorPosition(0, statusOriginRow + entries.Length);
            var errLine = $"{new string(' ', 4)}⚠ {errorLog.ErrorCount} 个错误，详情见 {Path.GetFileName(errorLogPath)}";
            Console.Write(errLine.PadRight(width));
        }

        Console.SetCursorPosition(0, statusOriginRow + newLineCount);
        prevStatusLines = newLineCount;
    }
    finally
    {
        Monitor.Exit(consoleLock);
    }
}

var statusTimer = new Timer(_ =>
{
    try { RenderStatus(); }
    catch { }
}, null, 2000, 2000);

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
    var runTask = Task.Run(() => recorder.RunAsync(), CancellationToken.None);
    runningTasks[sc.Id] = runTask;
}

void StopStreamer(string id)
{
    if (activeTasks.TryRemove(id, out var recorder))
        recorder.Stop();
    statuses.TryRemove(id, out _);
    runningTasks.TryRemove(id, out _);
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

Console.Write("\n正在等待录制任务结束...\n");

// 停止所有下载并等待编码+合并完成
var tasks = runningTasks.Values.ToArray();
foreach (var r in activeTasks.Values)
    r.Stop();

if (tasks.Length > 0)
{
    var waitCts = new CancellationTokenSource();
    waitCts.CancelAfter(TimeSpan.FromMinutes(10));
    try
    {
        await Task.WhenAll(tasks).WaitAsync(waitCts.Token);
    }
    catch (TimeoutException)
    {
        Console.Write("\n部分任务超时，强制退出...");
    }
    catch (OperationCanceledException) { }
    Console.Write("\n所有任务已完成");
}

await errorLog.FlushFileAsync();
errorLog.DisposeFile();

Console.WriteLine("\n再见!");

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

/// <summary>将所有 Console.Out 写入统一经过同一锁，防止与 SetCursorPosition 交错。</summary>
file sealed class LockedTextWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly object _lock;
    public LockedTextWriter(TextWriter inner, object lockObj)
    {
        _inner = inner;
        _lock = lockObj;
    }
    public override Encoding Encoding => _inner.Encoding;
    public override void Write(char value)
    {
        lock (_lock) { _inner.Write(value); }
    }
    public override void Write(char[] buffer, int index, int count)
    {
        lock (_lock) { _inner.Write(buffer, index, count); }
    }
    public override void Write(string? value)
    {
        lock (_lock) { _inner.Write(value); }
    }
    public override void WriteLine(string? value)
    {
        lock (_lock) { _inner.WriteLine(value); }
    }
    public override void WriteLine()
    {
        lock (_lock) { _inner.Write("\n"); }
    }
}

/// <summary>将 Console.Error 写入 error.log 文件，控制台仅反映错误计数。</summary>
file sealed class ErrorLogWriter : TextWriter
{
    private readonly StreamWriter _file;
    private int _errorCount;

    public override Encoding Encoding => Encoding.UTF8;
    public object Lock { get; }
    public int ErrorCount => _errorCount;

    public ErrorLogWriter(StreamWriter file, TextWriter? origOut = null)
    {
        _file = file;
        Lock = new object();
    }

    public override void Write(char value) { }
    public override void Write(string? value) { }
    public override void WriteLine() { }

    public override void WriteLine(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Interlocked.Increment(ref _errorCount);
        lock (Lock)
        {
            _file.Write($"[{DateTime.Now:HH:mm:ss}] ");
            _file.WriteLine(value);
            _file.Flush();
        }
    }

    public async Task FlushFileAsync() => await _file.FlushAsync();
    public void DisposeFile() => _file.Dispose();
}
