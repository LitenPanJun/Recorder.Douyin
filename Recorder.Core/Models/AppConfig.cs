namespace Recorder.Core.Models;

public class AppConfig
{
    public List<StreamerConfig> Streamers { get; set; } = new();
    public DefaultConfig Defaults { get; set; } = new();
}

public class StreamerConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string? Quality { get; set; }
    public bool? EnableHevc { get; set; }
    public int? Crf { get; set; }
    public double? SegmentDuration { get; set; }
    public string? OutputDir { get; set; }
}

public class DefaultConfig
{
    public string OutputDir { get; set; } = "./recordings";
    public string Quality { get; set; } = "原画";
    public bool EnableHevc { get; set; }
    public int Crf { get; set; } = 24;
    public double SegmentDuration { get; set; } = 10;
}

public class StreamerStatus
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = "等待中";
    public string Detail { get; set; } = string.Empty;
    public long BytesDownloaded { get; set; }
    public string SpeedFormatted { get; set; } = string.Empty;
    public TimeSpan Elapsed { get; set; }
}
