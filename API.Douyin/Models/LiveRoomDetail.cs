using Newtonsoft.Json.Linq;

namespace API.Douyin.Models;

public class LiveRoomDetail
{
    public string RoomId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Cover { get; init; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserAvatar { get; set; } = string.Empty;
    public int Online { get; init; }
    public bool IsLive { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Introduction { get; init; } = string.Empty;
    public JToken? RawData { get; init; }
    public Recorder.Shared.DanmakuArgs? DanmakuData { get; init; }
}
