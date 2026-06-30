namespace Douyin.Live.Services;

public class CookieService
{
    private readonly ISignatureProvider _signature;
    private string? _cookie;

    public CookieService(ISignatureProvider signature)
    {
        _signature = signature;
    }

    public async Task<string> GetCookieAsync()
    {
        if (!string.IsNullOrEmpty(_cookie))
            return _cookie;

        try
        {
            var setCookie = await Utils.HttpUtils.HeadAsync("https://live.douyin.com", new()
            {
                ["User-Agent"] = _signature.GetUserAgent(),
                ["Referer"] = "https://live.douyin.com"
            });

            var ttwid = setCookie
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(c => c.StartsWith("ttwid", StringComparison.OrdinalIgnoreCase));

            if (ttwid != null)
            {
                _cookie = ttwid;
            }
        }
        catch
        {
        }

        _cookie ??= "ttwid=1%7CB1qls3GdnZhUov9o2NxOMxxYS2ff6OSvEWbv0ytbES4%7C1680522049%7C280d802d6d478e3e78d0c807f7c487e7ffec0ae4e5fdd6a0fe74c3c6af149511";
        return _cookie;
    }

    public void SetCookie(string cookie)
    {
        _cookie = cookie;
    }
}
