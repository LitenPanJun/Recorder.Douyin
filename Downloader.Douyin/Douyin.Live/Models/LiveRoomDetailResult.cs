namespace Douyin.Live.Models;

public class LiveRoomDetailResult
{
    public LiveRoomDetail Detail { get; init; } = null!;
    public List<PlayQuality> Qualities { get; init; } = new();
}
