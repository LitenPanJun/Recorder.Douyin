namespace DouyinDanmaku.Models;

public class DouyinDanmakuArgs
{
    public string WebRid { get; set; }
    public string RoomId { get; set; }
    public string UserId { get; set; }
    public string Cookie { get; set; }

    public DouyinDanmakuArgs(string webRid, string roomId, string userId, string cookie)
    {
        WebRid = webRid;
        RoomId = roomId;
        UserId = userId;
        Cookie = cookie;
    }
}
