using Recorder.Shared;

namespace Downloader.Douyin.Models;

public class DownloadProgress
{
    public long BytesDownloaded { get; set; }
    public long? TotalBytes { get; set; }
    public double Percentage => TotalBytes.HasValue && TotalBytes.Value > 0
        ? Math.Round((double)BytesDownloaded / TotalBytes.Value * 100, 1)
        : 0;
    public double SpeedBytesPerSecond { get; set; }
    public string SpeedFormatted => FormatSpeed(SpeedBytesPerSecond);
    public string CurrentSegment { get; set; } = string.Empty;
    public int SegmentsCompleted { get; set; }
    public int? SegmentsTotal { get; set; }
    public bool IsCompleted { get; set; }
    public TimeSpan Elapsed { get; set; }
    public long BytesPerSecond => (long)SpeedBytesPerSecond;

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec >= 1024 * 1024)
            return $"{bytesPerSec / (1024 * 1024):F1} MB/s";
        if (bytesPerSec >= 1024)
            return $"{bytesPerSec / 1024:F1} KB/s";
        return $"{bytesPerSec:F0} B/s";
    }

    public override string ToString()
    {
        var total = TotalBytes.HasValue ? SharedUtils.FormatSize(TotalBytes.Value) : "未知";
        var downloaded = SharedUtils.FormatSize(BytesDownloaded);
        var pct = TotalBytes.HasValue ? $" {Percentage}%" : "";
        var seg = !string.IsNullOrEmpty(CurrentSegment)
            ? $" {CurrentSegment}"
            : SegmentsTotal.HasValue
                ? $" [分段 {SegmentsCompleted}/{SegmentsTotal}]"
                : "";
        var elapsed = $"耗时: {(int)Elapsed.TotalHours:D2}:{Elapsed.Minutes:D2}:{Elapsed.Seconds:D2}";
        return $"已下载: {downloaded}/{total}{pct} | 速度: {SpeedFormatted} | {elapsed}{seg}";
    }
}
