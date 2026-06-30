using API.Douyin.Models;
using API.Douyin.Services;

namespace API.Douyin;

public class DouyinLiveClient
{
    private readonly ApiService _api;
    private readonly CookieService _cookies;
    private readonly ISignatureProvider _signature;

    public DouyinLiveClient()
        : this(new SignatureService())
    {
    }

    public DouyinLiveClient(ISignatureProvider signature)
    {
        _signature = signature;
        _cookies = new CookieService(signature);
        _api = new ApiService(_cookies, signature);
    }

    public async Task<LiveRoomDetail> GetRoomDetailAsync(string roomId)
    {
        return await _api.GetRoomDetailAsync(roomId);
    }

    public async Task<LiveRoomDetail> GetRoomDetailByUserUniqueIdAsync(string uniqueId)
    {
        return await _api.GetRoomDetailByUserUniqueIdAsync(uniqueId);
    }

    public async Task<List<PlayQuality>> GetPlayQualitiesAsync(LiveRoomDetail detail)
    {
        return await _api.GetPlayQualitiesAsync(detail);
    }

    public async Task<LiveRoomDetailResult> GetRoomDetailWithQualitiesAsync(string roomId)
    {
        var detail = await GetRoomDetailAsync(roomId);
        var qualities = await GetPlayQualitiesAsync(detail);
        return new LiveRoomDetailResult
        {
            Detail = detail,
            Qualities = qualities
        };
    }

    public async Task<List<string>> GetPlayUrlsAsync(LiveRoomDetail detail, PlayQuality quality)
    {
        return new List<string>(quality.Urls);
    }

    public void SetCookie(string cookie)
    {
        _cookies.SetCookie(cookie);
    }
}
