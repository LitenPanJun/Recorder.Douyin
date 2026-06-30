namespace Douyin.Live.Models;

public class PlayQuality
{
    public string Name { get; init; } = string.Empty;
    public int Sort { get; init; }
    public List<string> Urls { get; init; } = new();
    public StreamType StreamType { get; init; } = StreamType.Flv;
}

public enum StreamType
{
    Flv,
    Hls
}
