using API.Douyin.Models;

namespace API.Douyin.Services;

public interface ILiveStatusService
{
    Task<LiveStatusInfo> GetLiveStatusAsync(string roomId);
}
