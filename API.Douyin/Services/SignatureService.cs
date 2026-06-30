using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Recorder.Shared;

namespace API.Douyin.Services;

public interface ISignatureProvider
{
    Task<string> GenerateABogusAsync(string url, string userAgent);
    string GenerateMsToken(int length = 107);
    string GetUserAgent();
}

public class SignatureService : ISignatureProvider
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0";

    private const string AbogusEndpoint = "https://dy.nsapps.cn/abogus";

    public string GetUserAgent() => DefaultUserAgent;

    public string GenerateMsToken(int length = 107)
    {
        return SharedUtils.GenerateMsToken(length);
    }

    public async Task<string> GenerateABogusAsync(string url, string userAgent)
    {
        try
        {
            var payload = JsonConvert.SerializeObject(new { url, userAgent });
            var resp = await HttpUtils.PostJsonAsync(AbogusEndpoint, payload);
            var obj = JObject.Parse(resp);
            return obj["data"]?["url"]?.ToString() ?? url;
        }
        catch
        {
            return url;
        }
    }
}
