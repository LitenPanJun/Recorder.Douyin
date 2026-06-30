namespace DouyinDanmaku.Models;

public enum LiveMessageType
{
    Chat,
    Gift,
    Online,
    Member,
    Like,
    Social,
    Unknown
}

public class LiveMessage
{
    public LiveMessageType Type { get; set; }
    public string UserName { get; set; } = "";
    public string Content { get; set; } = "";
    public string UserId { get; set; } = "";
    public object? Data { get; set; }
    public LiveMessageColor Color { get; set; } = LiveMessageColor.White;
    public string RoomId { get; set; } = "";
    public List<MessageImage> Images { get; set; } = new();

    public override string ToString()
    {
        return $"[{Type}] {UserName}: {Content}";
    }
}

public class LiveMessageColor
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    public LiveMessageColor(byte r, byte g, byte b) => (R, G, B) = (r, g, b);

    public static LiveMessageColor White => new(255, 255, 255);

    public static LiveMessageColor FromInt(int intColor)
    {
        var hex = intColor.ToString("x");
        if (hex.Length == 4) hex = "00" + hex;
        if (hex.Length >= 6)
        {
            if (hex.Length == 8) hex = hex[2..]; // skip alpha
            return new LiveMessageColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16)
            );
        }
        return White;
    }

    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

public class MessageImage
{
    public string Url { get; set; } = "";
    public ulong Width { get; set; }
    public ulong Height { get; set; }
    public string AvgColor { get; set; } = "";
    public bool IsAnimated { get; set; }
    public byte[]? CachedData { get; set; }
}

public class GiftInfo
{
    public string GiftName { get; set; } = "";
    public ulong GiftId { get; set; }
    public uint DiamondCount { get; set; }
    public uint ComboCount { get; set; }
    public uint RepeatCount { get; set; }
    public string Describe { get; set; } = "";
    public MessageImage? GiftImage { get; set; }
    public MessageImage? GiftIcon { get; set; }
    public string UserName { get; set; } = "";
    public string ToUserName { get; set; } = "";
}

public class OnlineInfo
{
    public long TotalUser { get; set; }
    public long Total { get; set; }
    public long Popularity { get; set; }
}
