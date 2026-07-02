using System.Net;
using System.Text;

namespace Recorder.Shared;

public static class HttpUtils
{
    private static readonly HttpClientHandler Handler = new()
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        UseCookies = false
    };

    private static readonly HttpClient Client = new(Handler)
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static async Task<string> GetStringAsync(string url, Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers != null)
        {
            foreach (var (key, value) in headers)
                request.Headers.TryAddWithoutValidation(key, SanitizeHeaderValue(value));
        }

        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public static async Task<string> HeadAsync(string url, Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, url);
        if (headers != null)
        {
            foreach (var (key, value) in headers)
                request.Headers.TryAddWithoutValidation(key, SanitizeHeaderValue(value));
        }

        using var response = await Client.SendAsync(request);
        return response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? string.Join("; ", cookies.Select(c => c.Split(';')[0]))
            : string.Empty;
    }

    public static async Task<string> PostJsonAsync(string url, string json, Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (headers != null)
        {
            foreach (var (key, value) in headers)
                request.Headers.TryAddWithoutValidation(key, SanitizeHeaderValue(value));
        }

        using var response = await Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public static async Task<HttpResponseMessage> GetResponseAsync(string url, Dictionary<string, string>? headers = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (headers != null)
        {
            foreach (var (key, value) in headers)
                request.Headers.TryAddWithoutValidation(key, SanitizeHeaderValue(value));
        }

        var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static string SanitizeHeaderValue(string value)
    {
        return value.Any(c => c > 127)
            ? new string(value.Where(c => c <= 127).ToArray())
            : value;
    }

    public static HttpClient CreateClient(TimeSpan timeout, DecompressionMethods decompression = DecompressionMethods.GZip | DecompressionMethods.Deflate)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = decompression
        };
        return new HttpClient(handler) { Timeout = timeout };
    }
}
