using API.Douyin.Models;

namespace API.Douyin.Services;

public class LiveStatusService : ILiveStatusService
{
    private readonly ApiService _api;

    public LiveStatusService(ApiService api)
    {
        _api = api;
    }

    public async Task<LiveStatusInfo> GetLiveStatusAsync(string roomId)
    {
        return await _api.GetLiveStatusAsync(roomId);
    }
}
