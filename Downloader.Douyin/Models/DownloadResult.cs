namespace Downloader.Douyin.Models;

public class DownloadResult
{
    public long TotalBytes { get; init; }
    public TimeSpan Elapsed { get; init; }
    public int SegmentCount { get; init; }
    public string OutputDirectory { get; init; } = string.Empty;
    public string BaseFileName { get; init; } = string.Empty;
    public bool HevcEncoded { get; init; }
    public List<string> SegmentFiles { get; init; } = new();

    public override string ToString()
    {
        var size = TotalBytes >= 1024L * 1024 * 1024
            ? $"{TotalBytes / (1024.0 * 1024 * 1024):F2} GB"
            : $"{TotalBytes / (1024.0 * 1024):F1} MB";
        var hevc = HevcEncoded ? " (已 HEVC 编码)" : "";
        return $"共 {SegmentCount} 个分段, {size}, 耗时 {Elapsed:hh\\:mm\\:ss}{hevc}";
    }
}
