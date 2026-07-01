using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using API.Douyin.Models;
using Recorder.Shared;

namespace API.Douyin.Services;

public class ApiService
{
    private const string Authority = "live.douyin.com";
    private const string Referer = "https://live.douyin.com";

    private readonly CookieService _cookieService;
    private readonly ISignatureProvider _signature;

    public ApiService(CookieService cookieService, ISignatureProvider signature)
    {
        _cookieService = cookieService;
        _signature = signature;
    }

    public async Task<LiveStatusInfo> GetLiveStatusAsync(string roomId)
    {
        try
        {
            if (roomId.Length <= 16)
            {
                var data = await GetRoomDataByApiAsync(roomId);
                var roomData = data["data"]?[0];
                return new LiveStatusInfo
                {
                    IsLive = roomData?["status"]?.ToObject<int>() == 2,
                    Title = roomData?["title"]?.ToString() ?? ""
                };
            }
            var roomJson = await GetRoomDataByRoomIdAsync(roomId);
            var room = roomJson["data"]?["room"];
            return new LiveStatusInfo
            {
                IsLive = room?["status"]?.ToObject<int>() == 2,
                Title = room?["title"]?.ToString() ?? ""
            };
        }
        catch
        {
            return new LiveStatusInfo();
        }
    }

    private async Task<Dictionary<string, string>> GetHeadersAsync(string? referer = null)
    {
        var cookie = await _cookieService.GetCookieAsync();
        return new()
        {
            ["User-Agent"] = _signature.GetUserAgent(),
            ["Referer"] = referer ?? Referer,
            ["Authority"] = Authority,
            ["Cookie"] = cookie
        };
    }

    public async Task<LiveRoomDetail> GetRoomDetailAsync(string roomId)
    {
        return roomId.Length <= 16
            ? await GetRoomDetailByWebRidAsync(roomId)
            : await GetRoomDetailByRoomIdAsync(roomId);
    }

    public async Task<LiveRoomDetail> GetRoomDetailByUserUniqueIdAsync(string uniqueId)
    {
        // 1) 用户主页解析
        try
        {
            var userState = await GetUserStateByUniqueIdAsync(uniqueId);
            var odin = userState["userStore"]?["odin"];
            if (odin != null)
            {
                var liveRoom = odin["live_room"];
                var secUid = odin["sec_uid"]?.ToString() ?? "";

                if (liveRoom != null)
                {
                    var idStr = liveRoom["id_str"]?.ToString();
                    var status = liveRoom["status"]?.ToObject<int>() ?? 0;
                    if (status == 2 && !string.IsNullOrEmpty(idStr))
                    {
                        var detail = await GetRoomDetailAsync(idStr);
                        detail.UserName = odin["nickname"]?.ToString() ?? detail.UserName;
                        detail.UserAvatar = odin["avatar_larger"]?["url_list"]?[0]?.ToString()
                                           ?? odin["avatar_thumb"]?["url_list"]?[0]?.ToString()
                                           ?? detail.UserAvatar;
                        return detail;
                    }
                }

                if (!string.IsNullOrEmpty(secUid))
                {
                    try
                    {
                        var roomJson = await GetRoomDataBySecUidAsync(secUid);
                        var roomId = roomJson["data"]?["room"]?["id_str"]?.ToString();
                        if (!string.IsNullOrEmpty(roomId))
                            return await GetRoomDetailAsync(roomId);
                    }
                    catch { /* ignore */ }
                }

                throw new Exception($"用户 '{uniqueId}' 当前未开播");
            }
        }
        catch
        {
        }

        // 2) 直接尝试将抖音号用作 web_rid（部分用户 web_rid 即为抖音号）
        try
        {
            var detail = await GetRoomDetailAsync(uniqueId);
            if (detail.IsLive)
                return detail;
        }
        catch
        {
        }

        // 3) 搜索 API 回退
        return await GetRoomDetailBySearchAsync(uniqueId);
    }

    private async Task<LiveRoomDetail> GetRoomDetailByRoomIdAsync(string roomId)
    {
        var roomJson = await GetRoomDataByRoomIdAsync(roomId);
        var webRid = roomJson["data"]?["room"]?["owner"]?["web_rid"]?.ToString();
        if (string.IsNullOrEmpty(webRid))
            throw new Exception("无法获取webRid");

        var userUniqueId = SharedUtils.GenerateRandomId(12);
        var room = roomJson["data"]?["room"];
        var owner = room?["owner"];
        var status = room?["status"]?.ToObject<int>() ?? 0;

        if (status == 4)
            return await GetRoomDetailByWebRidAsync(webRid);

        var isLive = status == 2;
        var cookie = await _cookieService.GetCookieAsync();

        return new LiveRoomDetail
        {
            RoomId = webRid,
            Title = room?["title"]?.ToString() ?? "",
            Cover = isLive ? room?["cover"]?["url_list"]?[0]?.ToString() ?? "" : "",
            UserName = owner?["nickname"]?.ToString() ?? "",
            UserAvatar = owner?["avatar_thumb"]?["url_list"]?[0]?.ToString() ?? "",
            Online = isLive ? room?["room_view_stats"]?["display_value"]?.ToObject<int>() ?? 0 : 0,
            IsLive = isLive,
            Url = $"https://live.douyin.com/{webRid}",
            Introduction = owner?["signature"]?.ToString() ?? "",
            RawData = isLive ? room?["stream_url"] : null,
            DanmakuData = new Recorder.Shared.DanmakuArgs
            {
                WebRid = webRid,
                RoomId = roomId,
                UserId = userUniqueId,
                Cookie = cookie
            }
        };
    }

    private async Task<LiveRoomDetail> GetRoomDetailByWebRidAsync(string webRid)
    {
        try
        {
            return await GetRoomDetailByApiAsync(webRid);
        }
        catch
        {
        }

        return await GetRoomDetailByHtmlAsync(webRid);
    }

    private async Task<LiveRoomDetail> GetRoomDetailByApiAsync(string webRid)
    {
        var data = await GetRoomDataByApiAsync(webRid);
        var roomData = data["data"]?[0];
        var userData = data["user"];
        var roomId = roomData?["id_str"]?.ToString() ?? "";
        var userUniqueId = SharedUtils.GenerateRandomId(12);
        var owner = roomData?["owner"];
        var isLive = roomData?["status"]?.ToObject<int>() == 2;
        var cookie = await _cookieService.GetCookieAsync();

        return new LiveRoomDetail
        {
            RoomId = webRid,
            Title = roomData?["title"]?.ToString() ?? "",
            Cover = isLive ? roomData?["cover"]?["url_list"]?[0]?.ToString() ?? "" : "",
            UserName = isLive
                ? owner?["nickname"]?.ToString() ?? ""
                : userData?["nickname"]?.ToString() ?? "",
            UserAvatar = isLive
                ? owner?["avatar_thumb"]?["url_list"]?[0]?.ToString() ?? ""
                : userData?["avatar_thumb"]?["url_list"]?[0]?.ToString() ?? "",
            Online = isLive
                ? roomData?["room_view_stats"]?["display_value"]?.ToObject<int>() ?? 0
                : 0,
            IsLive = isLive,
            Url = $"https://live.douyin.com/{webRid}",
            Introduction = owner?["signature"]?.ToString() ?? "",
            RawData = isLive ? roomData?["stream_url"] : null,
            DanmakuData = new Recorder.Shared.DanmakuArgs
            {
                WebRid = webRid,
                RoomId = roomId,
                UserId = userUniqueId,
                Cookie = cookie
            }
        };
    }

    private async Task<LiveRoomDetail> GetRoomDetailByHtmlAsync(string webRid)
    {
        var roomData = await GetRoomDataByHtmlAsync(webRid);
        var room = roomData["roomStore"]?["roomInfo"]?["room"];
        var owner = room?["owner"];
        var anchor = roomData["roomStore"]?["roomInfo"]?["anchor"];
        var roomId = room?["id_str"]?.ToString() ?? "";
        var userUniqueId = roomData["userStore"]?["odin"]?["user_unique_id"]?.ToString() ?? SharedUtils.GenerateRandomId(12);
        var isLive = room?["status"]?.ToObject<int>() == 2;
        var cookie = await _cookieService.GetCookieAsync();

        return new LiveRoomDetail
        {
            RoomId = webRid,
            Title = room?["title"]?.ToString() ?? "",
            Cover = isLive ? room?["cover"]?["url_list"]?[0]?.ToString() ?? "" : "",
            UserName = isLive
                ? owner?["nickname"]?.ToString() ?? ""
                : anchor?["nickname"]?.ToString() ?? "",
            UserAvatar = isLive
                ? owner?["avatar_thumb"]?["url_list"]?[0]?.ToString() ?? ""
                : anchor?["avatar_thumb"]?["url_list"]?[0]?.ToString() ?? "",
            Online = isLive
                ? room?["room_view_stats"]?["display_value"]?.ToObject<int>() ?? 0
                : 0,
            IsLive = isLive,
            Url = $"https://live.douyin.com/{webRid}",
            Introduction = owner?["signature"]?.ToString() ?? "",
            RawData = isLive ? room?["stream_url"] : null,
            DanmakuData = new Recorder.Shared.DanmakuArgs
            {
                WebRid = webRid,
                RoomId = roomId,
                UserId = userUniqueId,
                Cookie = cookie
            }
        };
    }

    private static string? ExtractBalancedJson(string text, int startIndex)
    {
        if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '{')
            return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = startIndex; i < text.Length; i++)
        {
            var c = text[i];

            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return text.Substring(startIndex, i - startIndex + 1); }
        }

        return null;
    }

    private async Task<JToken> GetUserStateByUniqueIdAsync(string uniqueId)
    {
        var cookie = await _cookieService.GetCookieAsync();

        // 单独获取 www.douyin.com 域的 cookie
        var wwwCookie = await HttpUtils.HeadAsync("https://www.douyin.com/", new()
        {
            ["User-Agent"] = _signature.GetUserAgent(),
            ["Referer"] = "https://www.douyin.com"
        });
        var mergedCookie = string.IsNullOrEmpty(wwwCookie) ? cookie : wwwCookie;

        var resp = await HttpUtils.GetStringAsync($"https://www.douyin.com/user/{uniqueId}", new()
        {
            ["User-Agent"] = _signature.GetUserAgent(),
            ["Referer"] = "https://www.douyin.com",
            ["Cookie"] = mergedCookie
        });

        // 1) __INITIAL_STATE__  (un-escaped JSON)
        var initMarker = "window.__INITIAL_STATE__";
        var initIdx = resp.IndexOf(initMarker, StringComparison.Ordinal);
        if (initIdx >= 0)
        {
            var braceIdx = resp.IndexOf('{', initIdx + initMarker.Length);
            var json = ExtractBalancedJson(resp, braceIdx);
            if (json != null)
            {
                var parsed = JObject.Parse(json);
                if (parsed["userStore"] != null)
                    return parsed;
            }
        }

        // 2) RENDER_DATA script tag — flexible pattern (type attr optional)
        var renderMatch = Regex.Match(resp,
            @"<script\s+id=""RENDER_DATA""[^>]*>([^<]+)</script>",
            RegexOptions.Singleline);
        if (renderMatch.Success)
        {
            var raw = renderMatch.Groups[1].Value;
            var decoded = Uri.UnescapeDataString(raw);

            // 有时 RENDER_DATA 是纯 JSON 字符串
            try { return JObject.Parse(decoded); } catch { }

            // 有时包含 "state":{...} 的嵌套结构
            var stateStart = decoded.IndexOf("\"state\":", StringComparison.Ordinal);
            if (stateStart >= 0)
            {
                var jsonStart = decoded.LastIndexOf('{', stateStart);
                if (jsonStart >= 0)
                {
                    var json = ExtractBalancedJson(decoded, jsonStart);
                    if (json != null)
                        return JObject.Parse(json);
                }
            }
        }

        // 3) RENDER_DATA with JSON inside text (unquoted)
        var renderMatch2 = Regex.Match(resp,
            @"RENDER_DATA\\x22\s*:\s*\\x22([^\\]+)\\x22",
            RegexOptions.Singleline);
        if (renderMatch2.Success)
        {
            var raw = renderMatch2.Groups[1].Value.Replace("\\x22", "\"");
            var decoded = Uri.UnescapeDataString(raw);
            try { return JObject.Parse(decoded); } catch { }
            var stateStart = decoded.IndexOf("\"state\":", StringComparison.Ordinal);
            if (stateStart >= 0)
            {
                var jsonStart = decoded.LastIndexOf('{', stateStart);
                if (jsonStart >= 0)
                {
                    var json = ExtractBalancedJson(decoded, jsonStart);
                    if (json != null)
                        return JObject.Parse(json);
                }
            }
        }

        // 4) Scrape all <script> tags for userStore pattern
        var scriptMatches = Regex.Matches(resp,
            @"<script[^>]*>([\s\S]*?)</script>",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        foreach (Match sm in scriptMatches)
        {
            var content = sm.Groups[1].Value;
            if (content.Contains("\"userStore\"") || content.Contains("userStore"))
            {
                var braceIdx = content.IndexOf('{');
                var json = ExtractBalancedJson(content, braceIdx);
                if (json != null)
                    return JObject.Parse(json);
            }
        }

        // 5) Escaped JSON style (same as live page)
        var escapedRegex = new Regex(
            @"\\\{\\\\\""userStore\\\\\"".*?\\]\\n",
            RegexOptions.Singleline);
        var escapedMatch = escapedRegex.Match(resp);
        if (escapedMatch.Success)
        {
            var json = escapedMatch.Groups[0].Value
                .Trim()
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("]\\n", "");
            return JObject.Parse(json);
        }

        throw new Exception("无法从用户主页解析数据");
    }

    private async Task<LiveRoomDetail> GetRoomDetailBySearchAsync(string keyword)
    {
        var query = new Dictionary<string, string>
        {
            ["device_platform"] = "webapp",
            ["aid"] = "6383",
            ["channel"] = "channel_pc_web",
            ["search_channel"] = "aweme_live",
            ["keyword"] = keyword,
            ["search_source"] = "switch_tab",
            ["query_correct_type"] = "1",
            ["is_filter_search"] = "0",
            ["from_group_id"] = "",
            ["offset"] = "0",
            ["count"] = "10",
            ["pc_client_type"] = "1",
            ["version_code"] = "170400",
            ["version_name"] = "17.4.0",
            ["cookie_enabled"] = "true",
            ["screen_width"] = "1980",
            ["screen_height"] = "1080",
            ["browser_language"] = "zh-CN",
            ["browser_platform"] = "Win32",
            ["browser_name"] = "Edge",
            ["browser_version"] = "125.0.0.0",
            ["browser_online"] = "true",
            ["engine_name"] = "Blink",
            ["engine_version"] = "125.0.0.0",
            ["os_name"] = "Windows",
            ["os_version"] = "10",
            ["cpu_core_num"] = "12",
            ["device_memory"] = "8",
            ["platform"] = "PC",
            ["downlink"] = "10",
            ["effective_type"] = "4g",
            ["round_trip_time"] = "100",
            ["webid"] = "7382872326016435738"
        };

        var url = $"https://www.douyin.com/aweme/v1/web/live/search/?{BuildQueryString(query)}";

        var cookie = await _cookieService.GetCookieAsync();
        var headers = new Dictionary<string, string>
        {
            ["accept"] = "application/json, text/plain, */*",
            ["accept-language"] = "zh-CN,zh;q=0.9,en;q=0.8",
            ["cookie"] = cookie,
            ["priority"] = "u=1, i",
            ["referer"] = $"https://www.douyin.com/search/{Uri.EscapeDataString(keyword)}?type=live",
            ["sec-ch-ua"] = "\"Microsoft Edge\";v=\"125\", \"Chromium\";v=\"125\", \"Not.A/Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\"",
            ["sec-fetch-dest"] = "empty",
            ["sec-fetch-mode"] = "cors",
            ["sec-fetch-site"] = "same-origin",
            ["user-agent"] = _signature.GetUserAgent()
        };

        var resp = await HttpUtils.GetStringAsync(url, headers);
        if (resp == "" || resp == "blocked")
            throw new Exception("抖音直播搜索被限制，请稍后再试");

        var json = JObject.Parse(resp);

        // 尝试多种 response 结构的 data 路径
        var dataItems = json["data"] as JArray
                       ?? json["data_list"] as JArray
                       ?? json["data"]?["data"] as JArray
                       ?? json["data"]?["list"] as JArray;

        if (dataItems == null || dataItems.Count == 0)
            throw new Exception($"搜索无结果，请确认用户 '{keyword}' 当前正在开播");

        foreach (var item in dataItems)
        {
            var rawdata = item["lives"]?["rawdata"]?.ToString();
            rawdata ??= item["rawdata"]?.ToString();

            if (string.IsNullOrEmpty(rawdata)) continue;

            JToken itemData;
            try { itemData = JObject.Parse(rawdata); } catch { continue; }

            var webRid = itemData["owner"]?["web_rid"]?.ToString();
            var uniqueId = itemData["owner"]?["unique_id"]?.ToString() ?? "";
            var nickname = itemData["owner"]?["nickname"]?.ToString() ?? "";
            var shortId = itemData["owner"]?["short_id"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(webRid)) continue;

            if (uniqueId.Equals(keyword, StringComparison.OrdinalIgnoreCase) ||
                nickname.Equals(keyword, StringComparison.OrdinalIgnoreCase) ||
                shortId.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return await GetRoomDetailAsync(webRid);
            }
        }

        // 放宽匹配：如果关键词是昵称的一部分
        foreach (var item in dataItems)
        {
            var rawdata = item["lives"]?["rawdata"]?.ToString();
            rawdata ??= item["rawdata"]?.ToString();
            if (string.IsNullOrEmpty(rawdata)) continue;

            JToken itemData;
            try { itemData = JObject.Parse(rawdata); } catch { continue; }

            var webRid = itemData["owner"]?["web_rid"]?.ToString();
            var nickname = itemData["owner"]?["nickname"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(webRid) &&
                nickname.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return await GetRoomDetailAsync(webRid);
            }
        }

        throw new Exception($"未找到抖音号 '{keyword}' 的直播，请确认用户当前正在开播且抖音号正确");
    }

    private async Task<JToken> GetRoomDataBySecUidAsync(string secUid)
    {
        var query = new Dictionary<string, string>
        {
            ["type_id"] = "0",
            ["live_id"] = "1",
            ["room_id"] = "",
            ["sec_user_id"] = secUid,
            ["version_code"] = "99.99.99",
            ["app_id"] = "6383"
        };

        var url = $"https://webcast.amemv.com/webcast/room/reflow/info/?{BuildQueryString(query)}";
        var resp = await HttpUtils.GetStringAsync(url, await GetHeadersAsync());
        return JObject.Parse(resp);
    }

    private async Task<JToken> GetRoomDataByApiAsync(string webRid)
    {
        var query = new Dictionary<string, string>
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
            ["screen_width"] = "1980",
            ["screen_height"] = "1080",
            ["browser_language"] = "zh-CN",
            ["browser_platform"] = "Win32",
            ["browser_name"] = "Edge",
            ["browser_version"] = "125.0.0.0",
            ["a_bogus"] = "0"
        };

        var baseUrl = $"https://live.douyin.com/webcast/room/web/enter/?{BuildQueryString(query)}";
        var signedUrl = await _signature.GenerateABogusAsync(baseUrl, _signature.GetUserAgent());

        var headers = await GetHeadersAsync($"https://live.douyin.com/{webRid}");
        var resp = await HttpUtils.GetStringAsync(signedUrl, headers);
        return JObject.Parse(resp)["data"] ?? throw new Exception("API返回数据为空");
    }

    private async Task<JToken> GetRoomDataByRoomIdAsync(string roomId)
    {
        var query = new Dictionary<string, string>
        {
            ["type_id"] = "0",
            ["live_id"] = "1",
            ["room_id"] = roomId,
            ["sec_user_id"] = "",
            ["version_code"] = "99.99.99",
            ["app_id"] = "6383"
        };

        var url = $"https://webcast.amemv.com/webcast/room/reflow/info/?{BuildQueryString(query)}";
        var resp = await HttpUtils.GetStringAsync(url, await GetHeadersAsync());
        return JObject.Parse(resp);
    }

    private async Task<JToken> GetRoomDataByHtmlAsync(string webRid)
    {
        var headCookie = await HttpUtils.HeadAsync($"https://live.douyin.com/{webRid}", new()
        {
            ["User-Agent"] = _signature.GetUserAgent(),
            ["Referer"] = Referer,
            ["Authority"] = Authority
        });

        var resp = await HttpUtils.GetStringAsync($"https://live.douyin.com/{webRid}", new()
        {
            ["User-Agent"] = _signature.GetUserAgent(),
            ["Referer"] = Referer,
            ["Authority"] = Authority,
            ["Cookie"] = headCookie
        });

        var regex = new Regex("\\{\\\\\"state\\\\\":\\{\\\\\"appStore.*?\\]\\\\n", RegexOptions.Singleline);
        var match = regex.Match(resp);
        var json = match.Success ? match.Groups[0].Value : "";

        if (string.IsNullOrEmpty(json))
        {
            var snippet = resp.Length > 200 ? resp[..200] + "..." : resp;
            throw new Exception($"无法从HTML解析直播间数据 (cookie={headCookie?.Length ?? 0}chars, resp={resp.Length}chars, 前200={snippet})");
        }

        json = json.Trim().Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("]\\n", "");
        return JObject.Parse(json)["state"] ?? throw new Exception("状态数据为空");
    }

    public async Task<List<PlayQuality>> GetPlayQualitiesAsync(LiveRoomDetail detail)
    {
        var qualities = new List<PlayQuality>();

        if (detail.RawData == null)
            return qualities;

        var data = detail.RawData;
        var pullData = data["live_core_sdk_data"]?["pull_data"];
        if (pullData == null)
            return qualities;

        var qualityList = pullData["options"]?["qualities"];
        var streamData = pullData["stream_data"]?.ToString() ?? "";

        if (!streamData.StartsWith("{"))
        {
            var flvList = (data["flv_pull_url"] as JToken)?.Values().Select(v => v.ToString()).ToList() ?? new();
            var hlsList = (data["hls_pull_url_map"] as JToken)?.Values().Select(v => v.ToString()).ToList() ?? new();

            foreach (var quality in qualityList ?? Enumerable.Empty<JToken>())
            {
                var level = quality["level"]?.ToObject<int>() ?? 0;
                var urls = new List<string>();

                var flvIndex = flvList.Count - level;
                if (flvIndex >= 0 && flvIndex < flvList.Count)
                    urls.Add(flvList[flvIndex]);

                var hlsIndex = hlsList.Count - level;
                if (hlsIndex >= 0 && hlsIndex < hlsList.Count)
                    urls.Add(hlsList[hlsIndex]);

                if (urls.Count > 0)
                {
                    qualities.Add(new PlayQuality
                    {
                        Name = quality["name"]?.ToString() ?? "",
                        Sort = level,
                        Urls = urls,
                        StreamType = urls.Any(u => u.Contains(".flv")) ? StreamType.Flv : StreamType.Hls
                    });
                }
            }
        }
        else
        {
            var qualityData = JObject.Parse(streamData)["data"] as JObject ?? new();

            foreach (var quality in qualityList ?? Enumerable.Empty<JToken>())
            {
                var urls = new List<string>();
                var sdkKey = quality["sdk_key"]?.ToString() ?? "";

                var flvUrl = qualityData[sdkKey]?["main"]?["flv"]?.ToString();
                if (!string.IsNullOrEmpty(flvUrl))
                    urls.Add(flvUrl);

                var hlsUrl = qualityData[sdkKey]?["main"]?["hls"]?.ToString();
                if (!string.IsNullOrEmpty(hlsUrl))
                    urls.Add(hlsUrl);

                if (urls.Count > 0)
                {
                    qualities.Add(new PlayQuality
                    {
                        Name = quality["name"]?.ToString() ?? "",
                        Sort = quality["level"]?.ToObject<int>() ?? 0,
                        Urls = urls,
                        StreamType = urls.Any(u => u.Contains(".flv")) ? StreamType.Flv : StreamType.Hls
                    });
                }
            }
        }

        return qualities.OrderByDescending(q => q.Sort).ToList();
    }

    private static string BuildQueryString(Dictionary<string, string> parameters)
    {
        return string.Join("&", parameters.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
    }
}
