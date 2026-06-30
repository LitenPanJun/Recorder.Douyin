using DouyinDanmaku.Models;

namespace DouyinDanmaku.Services;

public class ImageFetcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _cacheDir;

    public ImageFetcher(HttpClient? http = null, string? cacheDir = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36");
        _cacheDir = cacheDir ?? Path.Combine(Path.GetTempPath(), "DouyinDanmaku", "images");
        Directory.CreateDirectory(_cacheDir);
    }

    public async Task<byte[]?> FetchImageAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(url)));
        var cachePath = Path.Combine(_cacheDir, hash);
        if (File.Exists(cachePath))
            return await File.ReadAllBytesAsync(cachePath, ct);

        try
        {
            var data = await _http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(cachePath, data, ct);
            return data;
        }
        catch
        {
            return null;
        }
    }

    public async Task PrefetchImagesAsync(IEnumerable<MessageImage> images, CancellationToken ct = default)
    {
        var tasks = images
            .Where(img => !string.IsNullOrWhiteSpace(img.Url))
            .Select(async img =>
            {
                img.CachedData = await FetchImageAsync(img.Url, ct);
            });
        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    public void ClearCache()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, true);
            Directory.CreateDirectory(_cacheDir);
        }
    }
}
