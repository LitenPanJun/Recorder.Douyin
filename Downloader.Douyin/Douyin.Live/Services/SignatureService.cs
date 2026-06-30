using System.Security.Cryptography;
using System.Text;

namespace Douyin.Live.Services;

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
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var data = RandomNumberGenerator.GetBytes(length);
        var result = new char[length];
        for (var i = 0; i < length; i++)
        {
            result[i] = chars[data[i] % chars.Length];
        }
        return new string(result);
    }

    public async Task<string> GenerateABogusAsync(string url, string userAgent)
    {
        try
        {
            var payload = Newtonsoft.Json.JsonConvert.SerializeObject(new { url, userAgent });
            var resp = await Utils.HttpUtils.PostJsonAsync(AbogusEndpoint, payload);
            var obj = Newtonsoft.Json.Linq.JObject.Parse(resp);
            return obj["data"]?["url"]?.ToString() ?? url;
        }
        catch
        {
            return url;
        }
    }
}
