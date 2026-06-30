using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using DouyinDanmaku.Utils;

namespace DouyinDanmaku.Services;

public class DouyinRoomInfo
{
    public string WebRid { get; set; } = "";
    public string RoomId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string Title { get; set; } = "";
    public string UserName { get; set; } = "";
    public string UserAvatar { get; set; } = "";
    public bool Status { get; set; }
}

public class DouyinRoomResolver
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    public static async Task<DouyinRoomInfo> ResolveAsync(string webRid)
    {
        var cookie = await FetchCookieAsync();

        try
        {
            var result = await ResolveByApiAsync(webRid, cookie);
            if (result != null)
                return result;
        }
        catch { }

        try
        {
            var result = await ResolveByHtmlAsync(webRid, cookie);
            if (result != null)
                return result;
        }
        catch { }

        return new DouyinRoomInfo
        {
            WebRid = webRid,
            Cookie = cookie,
            UserId = SharedUtils.GenerateRandomId(12),
            Status = false,
        };
    }

    private static async Task<string> FetchCookieAsync()
    {
        using var client = MakeClient();
        try
        {
            var resp = await client.GetAsync("https://live.douyin.com/");
            foreach (var c in resp.Headers.GetValues("Set-Cookie").DefaultIfEmpty())
            {
                var m = Regex.Match(c ?? "", @"ttwid=[^;]+", RegexOptions.IgnoreCase);
                if (m.Success) return m.Value;
            }
        }
        catch { }
        return "";
    }

    private static async Task<DouyinRoomInfo?> ResolveByApiAsync(string webRid, string cookie)
    {
        var msToken = SharedUtils.GenerateMsToken();
        var queryParams = new Dictionary<string, string>
        {
            ["aid"] = "6383",
            ["app_name"] = "douyin_web",
            ["live_id"] = "1",
            ["device_platform"] = "web",
            ["enter_from"] = "web_live",
            ["web_rid"] = webRid,
            ["room_id_str"] = "",
            ["enter_source"] = "",
            ["Room-Enter-User-Login-Ab"] = "0",
            ["is_need_double_stream"] = "false",
            ["cookie_enabled"] = "true",
            ["screen_width"] = "1920",
            ["screen_height"] = "1080",
            ["browser_language"] = "zh-CN",
            ["browser_platform"] = "Win32",
            ["browser_name"] = "Chrome",
            ["browser_version"] = "131.0.0.0",
            ["msToken"] = msToken,
        };

        var queryString = string.Join("&", queryParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        string aBogus;
        try
        {
            aBogus = DouyinSign.GetABogusSignature(queryString, UserAgent);
        }
        catch
        {
            return null;
        }

        var signedQuery = $"{queryString}&a_bogus={Uri.EscapeDataString(aBogus)}";
        var url = $"https://live.douyin.com/webcast/room/web/enter/?{signedQuery}";

        using var client = MakeClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("Referer", "https://live.douyin.com/");

        HttpResponseMessage resp;
        try
        {
            resp = await client.SendAsync(request);
        }
        catch
        {
            return null;
        }

        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data))
            return null;

        if (!data.TryGetProperty("data", out var roomList) || roomList.GetArrayLength() == 0)
            return null;

        var room = roomList[0];
        var roomId = room.GetProperty("id_str").GetString() ?? "";
        var statusCode = room.GetProperty("status").GetInt32();
        var title = GetString(room, "title");
        var userName = "";
        try
        {
            var owner = room.GetProperty("owner");
            userName = GetString(owner, "nickname");
        }
        catch { }

        var userId = "";
        try
        {
            var userInfo = data.GetProperty("user");
            userId = GetString(userInfo, "unique_id");
            if (string.IsNullOrEmpty(userId))
                userId = GetString(userInfo, "id_str");
        }
        catch { }
        if (string.IsNullOrEmpty(userId))
            userId = SharedUtils.GenerateRandomId(12);

        return new DouyinRoomInfo
        {
            WebRid = webRid,
            RoomId = roomId,
            UserId = userId,
            Cookie = cookie,
            Title = title,
            UserName = userName,
            Status = statusCode == 2,
        };
    }

    private static async Task<DouyinRoomInfo?> ResolveByHtmlAsync(string webRid, string cookie)
    {
        using var client = MakeClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"https://live.douyin.com/{webRid}");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);

        var resp = await client.SendAsync(request);
        var html = await resp.Content.ReadAsStringAsync();

        var roomId = ExtractRoomId(html);
        if (string.IsNullOrEmpty(roomId))
            return null;

        var userName = ExtractField(html, "nickname") ?? "";
        var title = ExtractField(html, "title") ?? "";
        var userId = ExtractField(html, "user_unique_id") ?? ExtractField(html, "unique_id") ?? SharedUtils.GenerateRandomId(12);

        return new DouyinRoomInfo
        {
            WebRid = webRid,
            RoomId = roomId,
            UserId = userId,
            Cookie = cookie,
            Title = title,
            UserName = userName,
            Status = true,
        };
    }

    private static HttpClient MakeClient()
    {
        var h = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var c = new HttpClient(h) { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return c;
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) ? v.GetString() ?? "" : "";

    private static string? ExtractRoomId(string html)
    {
        var m = Regex.Match(html, @"""id_str""\s*:\s*""(\d{18,20})""");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(html, @"""room_id""\s*:\s*""(\d{18,20})""");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(html, @"room_id%22%3A%22(\d{18,20})%22", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(html, @"id_str%22%3A%22(\d{18,20})%22", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;

        foreach (Match sm in Regex.Matches(html, @"<script[^>]*>([^<]{200,})</script>", RegexOptions.Singleline))
        {
            var raw = sm.Groups[1].Value.Trim();
            if (!raw.StartsWith("%7B", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var decoded = Uri.UnescapeDataString(raw);
                using var doc = JsonDocument.Parse(decoded);
                if (doc.RootElement.TryGetProperty("roomStore", out var rs) &&
                    rs.TryGetProperty("roomInfo", out var ri) &&
                    ri.TryGetProperty("room", out var room) &&
                    room.TryGetProperty("id_str", out var id))
                    return id.GetString();
            }
            catch { }
        }
        return null;
    }

    private static string? ExtractField(string html, string key)
    {
        var m = Regex.Match(html, $@"""{key}""\s*:\s*""([^""]+)""");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(html, $@"{key}%22%3A%22([^%]+)%22", RegexOptions.IgnoreCase);
        if (m.Success) return Uri.UnescapeDataString(m.Groups[1].Value);
        return null;
    }
}
