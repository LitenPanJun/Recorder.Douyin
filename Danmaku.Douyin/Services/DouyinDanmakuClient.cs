using System.IO.Compression;
using System.Text;
using DouyinDanmaku.Models;
using ProtoBuf;

namespace DouyinDanmaku.Services;

public class DouyinDanmakuClient : IDisposable
{
    private const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/116.0.5845.97 Safari/537.36";

    private const string DefaultServerUrl =
        "wss://webcast3-ws-web-lq.douyin.com/webcast/im/push/v2/";

    private WebSocketClient? _ws;
    private DouyinDanmakuArgs? _args;
    private CancellationTokenSource? _cts;

    public event Action<LiveMessage>? OnMessage;
    public event Action<string>? OnClose;
    public event Action? OnReady;

    public string UserAgent { get; set; } = DefaultUserAgent;
    public int HeartbeatIntervalMs { get; set; } = 10000;

    public async Task StartAsync(DouyinDanmakuArgs args)
    {
        _args = args;
        _cts = new CancellationTokenSource();

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var queryParams = new Dictionary<string, string>
        {
            ["app_name"] = "douyin_web",
            ["version_code"] = "180800",
            ["webcast_sdk_version"] = "1.3.0",
            ["update_version_code"] = "1.3.0",
            ["compress"] = "gzip",
            ["cursor"] = $"h-1_t-{ts}_r-1_d-1_u-1",
            ["host"] = "https://live.douyin.com",
            ["aid"] = "6383",
            ["live_id"] = "1",
            ["did_rule"] = "3",
            ["debug"] = "false",
            ["maxCacheMessageNumber"] = "20",
            ["endpoint"] = "live_pc",
            ["support_wrds"] = "1",
            ["im_path"] = "/webcast/im/fetch/",
            ["user_unique_id"] = args.UserId,
            ["device_platform"] = "web",
            ["cookie_enabled"] = "true",
            ["screen_width"] = "1920",
            ["screen_height"] = "1080",
            ["browser_language"] = "zh-CN",
            ["browser_platform"] = "Win32",
            ["browser_name"] = "Mozilla",
            ["browser_version"] = UserAgent.Replace("Mozilla/", ""),
            ["browser_online"] = "true",
            ["tz_name"] = "Asia/Shanghai",
            ["identity"] = "audience",
            ["room_id"] = args.RoomId,
            ["heartbeatDuration"] = "0",
        };

        var uriBuilder = new UriBuilder(DefaultServerUrl) { Scheme = "wss" };
        var query = string.Join("&", queryParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        uriBuilder.Query = query;

        var sign = DouyinSign.GetMSSDKSignature(args.RoomId, args.UserId, UserAgent);
        while (sign.Contains('-') || sign.Contains('='))
        {
            sign = DouyinSign.GetMSSDKSignature(args.RoomId, args.UserId, UserAgent);
        }
        var fullUrl = $"{uriBuilder}&signature={sign}";
        var backupUrl = fullUrl.Replace("webcast3-ws-web-lq", "webcast5-ws-web-lf");

        _ws = new WebSocketClient(
            url: fullUrl,
            backupUrl: backupUrl,
            heartbeatIntervalMs: HeartbeatIntervalMs,
            headers: new Dictionary<string, string>
            {
                ["User-Agent"] = UserAgent,
                ["Cookie"] = args.Cookie,
                ["Origin"] = "https://live.douyin.com",
            });

        _ws.OnMessage += OnWsMessage;
        _ws.OnReady += OnWsReady;
        _ws.OnHeartbeat += OnWsHeartbeat;
        _ws.OnClose += msg => OnClose?.Invoke(msg);
        _ws.OnReconnect += () => OnClose?.Invoke("与服务器断开连接，正在尝试重连");

        await _ws.ConnectAsync();
    }

    private void OnWsReady()
    {
        OnReady?.Invoke();
        SendJoinRoom();
    }

    private void OnWsHeartbeat()
    {
        SendFrame(new PushFrame { PayloadType = "hb" });
    }

    private void SendJoinRoom()
    {
        SendFrame(new PushFrame { PayloadType = "sr" });
    }

    private void SendFrame(PushFrame frame)
    {
        using var ms = new MemoryStream();
        Serializer.Serialize(ms, frame);
        _ws?.Send(ms.ToArray());
    }

    private void OnWsMessage(byte[] data)
    {
        PushFrame wssPackage;
        Response payloadPackage;

        try
        {
            wssPackage = Serializer.Deserialize<PushFrame>(new MemoryStream(data));
        }
        catch
        {
            return;
        }

        if (wssPackage.PayloadType == "hb" || wssPackage.Payload.Length == 0)
            return;

        byte[] payloadBytes;
        try
        {
            payloadBytes = GzipDecompress(wssPackage.Payload);
        }
        catch
        {
            return;
        }

        try
        {
            payloadPackage = Serializer.Deserialize<Response>(new MemoryStream(payloadBytes));
        }
        catch
        {
            return;
        }

        if (payloadPackage.NeedAck)
        {
            SendAck(wssPackage.LogId, payloadPackage.InternalExt);
        }

        foreach (var msg in payloadPackage.MessagesList)
        {
            try
            {
                DispatchMessage(msg);
            }
            catch { }
        }
    }

    private static readonly HashSet<string> IgnoredMethods =
    [
        "WebcastRoomStatsMessage",
        "WebcastResidentGuestMessage",
        "WebcastLowPcuGuideMessage",
        "WebcastLowPcuGuideChatMessage",
        "WebcastInRoomBannerMessage",
        "WebcastRoomRankMessage",
        "WebcastRoomCommentTopicMessage",
    ];

    private void DispatchMessage(Message msg)
    {
        switch (msg.Method)
        {
            case "WebcastChatMessage":
                HandleChatMessage(msg.Payload);
                break;
            case "WebcastRoomUserSeqMessage":
                HandleRoomUserSeqMessage(msg.Payload);
                break;
            case "WebcastGiftMessage":
                HandleGiftMessage(msg.Payload);
                break;
            case "WebcastMemberMessage":
                HandleMemberMessage(msg.Payload);
                break;
            case "WebcastLikeMessage":
                HandleLikeMessage(msg.Payload);
                break;
            case "WebcastSocialMessage":
                HandleSocialMessage(msg.Payload);
                break;
            default:
                if (msg.Method.StartsWith("Webcast", StringComparison.Ordinal) &&
                    !IgnoredMethods.Contains(msg.Method))
                {
                    var unknown = new LiveMessage
                    {
                        Type = LiveMessageType.Unknown,
                        Content = $"[{msg.Method}] (未处理, {msg.Payload.Length} bytes)",
                    };
                    OnMessage?.Invoke(unknown);
                }
                break;
        }
    }

    private void HandleChatMessage(byte[] payload)
    {
        var chat = Serializer.Deserialize<ChatMessage>(new MemoryStream(payload));
        var msg = new LiveMessage
        {
            Type = LiveMessageType.Chat,
            UserName = chat.User?.NickName ?? "",
            Content = chat.Content,
            UserId = chat.User?.Id.ToString() ?? "",
            RoomId = _args?.RoomId ?? "",
            Color = ParseColor(chat.FullScreenTextColor),
        };

        if (chat.RtfContent?.PiecesList.Count > 0)
        {
            var sb = new StringBuilder();
            foreach (var piece in chat.RtfContent.PiecesList)
            {
                if (piece.ImageValue?.Image != null)
                {
                    var img = piece.ImageValue.Image;
                    var url = img.UrlListList.FirstOrDefault() ?? "";
                    if (!string.IsNullOrEmpty(url))
                    {
                        msg.Images.Add(new MessageImage
                        {
                            Url = url,
                            Width = img.Width,
                            Height = img.Height,
                            IsAnimated = img.IsAnimated,
                            AvgColor = img.AvgColor,
                        });
                    }
                    sb.Append(piece.StringValue);
                }
                else if (piece.UserValue?.User != null)
                {
                    sb.Append($"@{piece.UserValue.User.NickName}");
                }
                else if (!string.IsNullOrEmpty(piece.StringValue))
                {
                    sb.Append(piece.StringValue);
                }
            }
            if (sb.Length > 0)
                msg.Content = sb.ToString();
        }

        OnMessage?.Invoke(msg);
    }

    private void HandleRoomUserSeqMessage(byte[] payload)
    {
        var seq = Serializer.Deserialize<RoomUserSeqMessage>(new MemoryStream(payload));
        var msg = new LiveMessage
        {
            Type = LiveMessageType.Online,
            Content = $"在线人数: {seq.Total}",
            Data = new OnlineInfo
            {
                TotalUser = seq.Total,
                Total = seq.Total,
                Popularity = seq.Popularity,
            },
        };
        OnMessage?.Invoke(msg);
    }

    private void HandleGiftMessage(byte[] payload)
    {
        var gift = Serializer.Deserialize<GiftMessage>(new MemoryStream(payload));
        var giftInfo = new GiftInfo
        {
            GiftName = gift.Gift?.Name ?? "",
            GiftId = gift.GiftId,
            DiamondCount = gift.Gift?.DiamondCount ?? 0,
            ComboCount = (uint)gift.ComboCount,
            RepeatCount = (uint)gift.RepeatCount,
            Describe = gift.Gift?.Describe ?? "",
            UserName = gift.User?.NickName ?? "",
            ToUserName = gift.ToUser?.NickName ?? "",
        };

        if (gift.Gift?.Image?.UrlListList.Count > 0)
        {
            giftInfo.GiftImage = new MessageImage
            {
                Url = gift.Gift.Image.UrlListList[0],
                Width = gift.Gift.Image.Width,
                Height = gift.Gift.Image.Height,
            };
        }
        if (gift.Gift?.Icon?.UrlListList.Count > 0)
        {
            giftInfo.GiftIcon = new MessageImage
            {
                Url = gift.Gift.Icon.UrlListList[0],
            };
        }

        var msg = new LiveMessage
        {
            Type = LiveMessageType.Gift,
            UserName = gift.User?.NickName ?? "",
            Content = $"{gift.User?.NickName} 赠送了 {gift.RepeatCount} 个{gift.Gift?.Name}",
            Data = giftInfo,
        };

        if (giftInfo.GiftImage != null)
            msg.Images.Add(giftInfo.GiftImage);

        if (gift.Gift?.Icon != null)
            msg.Images.Add(giftInfo.GiftIcon!);

        OnMessage?.Invoke(msg);
    }

    private void HandleMemberMessage(byte[] payload)
    {
        var member = Serializer.Deserialize<MemberMessage>(new MemoryStream(payload));
        var msg = new LiveMessage
        {
            Type = LiveMessageType.Member,
            UserName = member.User?.NickName ?? "",
            Content = $"{member.User?.NickName} 进入直播间",
        };
        OnMessage?.Invoke(msg);
    }

    private void HandleLikeMessage(byte[] payload)
    {
        var like = Serializer.Deserialize<LikeMessage>(new MemoryStream(payload));
        var msg = new LiveMessage
        {
            Type = LiveMessageType.Like,
            UserName = like.User?.NickName ?? "",
            Content = $"{like.User?.NickName} 点赞了 {like.Count} 次",
            Data = new { Count = like.Count, Total = like.Total },
        };
        OnMessage?.Invoke(msg);
    }

    private void HandleSocialMessage(byte[] payload)
    {
        var social = Serializer.Deserialize<SocialMessage>(new MemoryStream(payload));
        var action = social.Action switch
        {
            1 => "关注",
            2 => "分享",
            _ => "互动"
        };
        var msg = new LiveMessage
        {
            Type = LiveMessageType.Social,
            UserName = social.User?.NickName ?? "",
            Content = $"{social.User?.NickName} {action}了直播间",
        };
        OnMessage?.Invoke(msg);
    }

    private void SendAck(ulong logId, string internalExt)
    {
        var frame = new PushFrame
        {
            PayloadType = "ack",
            LogId = logId,
        };
        if (!string.IsNullOrEmpty(internalExt))
        {
            frame.Payload = Encoding.UTF8.GetBytes(internalExt);
        }
        SendFrame(frame);
    }

    private static byte[] GzipDecompress(byte[] data)
    {
        using var compressed = new MemoryStream(data);
        using var decompressed = new MemoryStream();
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        gzip.CopyTo(decompressed);
        return decompressed.ToArray();
    }

    private static LiveMessageColor ParseColor(string colorStr)
    {
        if (string.IsNullOrEmpty(colorStr)) return LiveMessageColor.White;
        if (colorStr.StartsWith("#"))
            colorStr = colorStr[1..];
        if (colorStr.Length == 6 && int.TryParse(colorStr,
                System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            return LiveMessageColor.FromInt(rgb);
        return LiveMessageColor.White;
    }

    public async Task StopAsync()
    {
        if (_ws != null)
        {
            _ws.OnMessage -= OnWsMessage;
            _ws.OnReady -= OnWsReady;
            _ws.OnHeartbeat -= OnWsHeartbeat;
            _ws.Close();
        }
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (_ws != null)
        {
            _ws.OnMessage -= OnWsMessage;
            _ws.OnReady -= OnWsReady;
            _ws.OnHeartbeat -= OnWsHeartbeat;
        }
        _ws?.Dispose();
        _cts?.Dispose();
        _cts = null;
    }
}
