using ProtoBuf;

namespace DouyinDanmaku.Models;

[ProtoContract]
public class PushFrame
{
    [ProtoMember(1)] public ulong SeqId { get; set; }
    [ProtoMember(2)] public ulong LogId { get; set; }
    [ProtoMember(3)] public ulong Service { get; set; }
    [ProtoMember(4)] public ulong Method { get; set; }
    [ProtoMember(5)] public List<HeadersList> HeadersList { get; set; } = new();
    [ProtoMember(6)] public string PayloadEncoding { get; set; } = "";
    [ProtoMember(7)] public string PayloadType { get; set; } = "";
    [ProtoMember(8)] public byte[] Payload { get; set; } = Array.Empty<byte>();
}

[ProtoContract]
public class HeadersList
{
    [ProtoMember(1)] public string Key { get; set; } = "";
    [ProtoMember(2)] public string Value { get; set; } = "";
}

[ProtoContract]
public class Response
{
    [ProtoMember(1)] public List<Message> MessagesList { get; set; } = new();
    [ProtoMember(2)] public string Cursor { get; set; } = "";
    [ProtoMember(3)] public ulong FetchInterval { get; set; }
    [ProtoMember(4)] public ulong Now { get; set; }
    [ProtoMember(5)] public string InternalExt { get; set; } = "";
    [ProtoMember(6)] public uint FetchType { get; set; }
    [ProtoMember(7)] [ProtoMap] public Dictionary<string, string> RouteParams { get; set; } = new();
    [ProtoMember(8)] public ulong HeartbeatDuration { get; set; }
    [ProtoMember(9)] public bool NeedAck { get; set; }
    [ProtoMember(10)] public string PushServer { get; set; } = "";
    [ProtoMember(11)] public string LiveCursor { get; set; } = "";
    [ProtoMember(12)] public bool HistoryNoMore { get; set; }
}

[ProtoContract]
public class Message
{
    [ProtoMember(1)] public string Method { get; set; } = "";
    [ProtoMember(2)] public byte[] Payload { get; set; } = Array.Empty<byte>();
    [ProtoMember(3)] public long MsgId { get; set; }
    [ProtoMember(4)] public int MsgType { get; set; }
    [ProtoMember(5)] public long Offset { get; set; }
    [ProtoMember(6)] public bool NeedWrdsStore { get; set; }
    [ProtoMember(7)] public long WrdsVersion { get; set; }
    [ProtoMember(8)] public string WrdsSubKey { get; set; } = "";
}

[ProtoContract]
public class Common
{
    [ProtoMember(1)] public string Method { get; set; } = "";
    [ProtoMember(2)] public ulong MsgId { get; set; }
    [ProtoMember(3)] public ulong RoomId { get; set; }
    [ProtoMember(4)] public ulong CreateTime { get; set; }
    [ProtoMember(5)] public uint Monitor { get; set; }
    [ProtoMember(6)] public bool IsShowMsg { get; set; }
    [ProtoMember(7)] public string Describe { get; set; } = "";
    [ProtoMember(9)] public ulong FoldType { get; set; }
    [ProtoMember(10)] public ulong AnchorFoldType { get; set; }
    [ProtoMember(11)] public ulong PriorityScore { get; set; }
    [ProtoMember(12)] public string LogId { get; set; } = "";
    [ProtoMember(15)] public User? User { get; set; }
    [ProtoMember(17)] public ulong AnchorFoldTypeV2 { get; set; }
    [ProtoMember(21)] public ulong ChannelId { get; set; }
    [ProtoMember(22)] public ulong DiffSei2AbsSecond { get; set; }
    [ProtoMember(23)] public ulong AnchorFoldDuration { get; set; }
}

[ProtoContract]
public class User
{
    [ProtoMember(1)] public ulong Id { get; set; }
    [ProtoMember(2)] public ulong ShortId { get; set; }
    [ProtoMember(3)] public string NickName { get; set; } = "";
    [ProtoMember(4)] public uint Gender { get; set; }
    [ProtoMember(5)] public string Signature { get; set; } = "";
    [ProtoMember(6)] public uint Level { get; set; }
    [ProtoMember(9)] public Image? AvatarThumb { get; set; }
    [ProtoMember(10)] public Image? AvatarMedium { get; set; }
    [ProtoMember(11)] public Image? AvatarLarge { get; set; }
    [ProtoMember(12)] public bool Verified { get; set; }
    [ProtoMember(22)] public FollowInfo? FollowInfo { get; set; }
    [ProtoMember(28)] public Image? Medal { get; set; }
    [ProtoMember(38)] public string DisplayId { get; set; } = "";
    [ProtoMember(46)] public string SecUid { get; set; } = "";
    [ProtoMember(1028)] public string IdStr { get; set; } = "";
}

[ProtoContract]
public class FollowInfo
{
    [ProtoMember(1)] public ulong FollowingCount { get; set; }
    [ProtoMember(2)] public ulong FollowerCount { get; set; }
    [ProtoMember(3)] public ulong FollowStatus { get; set; }
    [ProtoMember(4)] public ulong PushStatus { get; set; }
    [ProtoMember(6)] public string FollowerCountStr { get; set; } = "";
    [ProtoMember(7)] public string FollowingCountStr { get; set; } = "";
}

[ProtoContract]
public class Image
{
    [ProtoMember(1)] public List<string> UrlListList { get; set; } = new();
    [ProtoMember(2)] public string Uri { get; set; } = "";
    [ProtoMember(3)] public ulong Height { get; set; }
    [ProtoMember(4)] public ulong Width { get; set; }
    [ProtoMember(5)] public string AvgColor { get; set; } = "";
    [ProtoMember(6)] public uint ImageType { get; set; }
    [ProtoMember(7)] public string OpenWebUrl { get; set; } = "";
    [ProtoMember(9)] public bool IsAnimated { get; set; }
}

[ProtoContract]
public class ChatMessage
{
    [ProtoMember(1)] public Common? Common { get; set; }
    [ProtoMember(2)] public User? User { get; set; }
    [ProtoMember(3)] public string Content { get; set; } = "";
    [ProtoMember(4)] public bool VisibleToSender { get; set; }
    [ProtoMember(5)] public Image? BackgroundImage { get; set; }
    [ProtoMember(6)] public string FullScreenTextColor { get; set; } = "";
    [ProtoMember(7)] public Image? BackgroundImageV2 { get; set; }
    [ProtoMember(8)] public PublicAreaCommon? PublicAreaCommon { get; set; }
    [ProtoMember(9)] public Image? GiftImage { get; set; }
    [ProtoMember(15)] public ulong EventTime { get; set; }
    [ProtoMember(22)] public Text? RtfContent { get; set; }
}

[ProtoContract]
public class PublicAreaCommon
{
    [ProtoMember(1)] public Image? UserLabel { get; set; }
    [ProtoMember(2)] public ulong UserConsumeInRoom { get; set; }
    [ProtoMember(3)] public ulong UserSendGiftCntInRoom { get; set; }
}

[ProtoContract]
public class RoomUserSeqMessage
{
    [ProtoMember(1)] public Common? Common { get; set; }
    [ProtoMember(2)] public List<RoomUserSeqMessageContributor> RanksList { get; set; } = new();
    [ProtoMember(3)] public long Total { get; set; }
    [ProtoMember(4)] public string PopStr { get; set; } = "";
    [ProtoMember(5)] public List<RoomUserSeqMessageContributor> SeatsList { get; set; } = new();
    [ProtoMember(6)] public long Popularity { get; set; }
    [ProtoMember(7)] public long TotalUser { get; set; }
    [ProtoMember(8)] public string TotalUserStr { get; set; } = "";
    [ProtoMember(9)] public string TotalStr { get; set; } = "";
    [ProtoMember(10)] public string OnlineUserForAnchor { get; set; } = "";
    [ProtoMember(11)] public string TotalPvForAnchor { get; set; } = "";
    [ProtoMember(12)] public string UpRightStatsStr { get; set; } = "";
    [ProtoMember(13)] public string UpRightStatsStrComplete { get; set; } = "";
}

[ProtoContract]
public class RoomUserSeqMessageContributor
{
    [ProtoMember(1)] public ulong Score { get; set; }
    [ProtoMember(2)] public User? User { get; set; }
    [ProtoMember(3)] public ulong Rank { get; set; }
    [ProtoMember(4)] public ulong Delta { get; set; }
    [ProtoMember(5)] public bool IsHidden { get; set; }
    [ProtoMember(6)] public string ScoreDescription { get; set; } = "";
}

[ProtoContract]
public class GiftMessage
{
    [ProtoMember(1)] public Common? Common { get; set; }
    [ProtoMember(2)] public ulong GiftId { get; set; }
    [ProtoMember(3)] public ulong FanTicketCount { get; set; }
    [ProtoMember(4)] public ulong GroupCount { get; set; }
    [ProtoMember(5)] public ulong RepeatCount { get; set; }
    [ProtoMember(6)] public ulong ComboCount { get; set; }
    [ProtoMember(7)] public User? User { get; set; }
    [ProtoMember(8)] public User? ToUser { get; set; }
    [ProtoMember(9)] public uint RepeatEnd { get; set; }
    [ProtoMember(10)] public TextEffect? TextEffect { get; set; }
    [ProtoMember(11)] public ulong GroupId { get; set; }
    [ProtoMember(15)] public GiftStruct? Gift { get; set; }
    [ProtoMember(29)] public ulong TotalCount { get; set; }
    [ProtoMember(33)] public ulong SendTime { get; set; }
}

[ProtoContract]
public class GiftStruct
{
    [ProtoMember(1)] public Image? Image { get; set; }
    [ProtoMember(2)] public string Describe { get; set; } = "";
    [ProtoMember(4)] public ulong Duration { get; set; }
    [ProtoMember(5)] public ulong Id { get; set; }
    [ProtoMember(11)] public uint Type { get; set; }
    [ProtoMember(12)] public uint DiamondCount { get; set; }
    [ProtoMember(16)] public string Name { get; set; } = "";
    [ProtoMember(21)] public Image? Icon { get; set; }
}

[ProtoContract]
public class TextEffect
{
    [ProtoMember(1)] public TextEffectDetail? Portrait { get; set; }
    [ProtoMember(2)] public TextEffectDetail? Landscape { get; set; }
}

[ProtoContract]
public class TextEffectDetail
{
    [ProtoMember(1)] public Text? Text { get; set; }
    [ProtoMember(3)] public Image? Background { get; set; }
    [ProtoMember(4)] public uint Start { get; set; }
    [ProtoMember(5)] public uint Duration { get; set; }
    [ProtoMember(6)] public uint X { get; set; }
    [ProtoMember(7)] public uint Y { get; set; }
    [ProtoMember(8)] public uint Width { get; set; }
    [ProtoMember(9)] public uint Height { get; set; }
}

[ProtoContract]
public class MemberMessage
{
    [ProtoMember(1)] public Common? Common { get; set; }
    [ProtoMember(2)] public User? User { get; set; }
    [ProtoMember(3)] public ulong MemberCount { get; set; }
    [ProtoMember(7)] public ulong RankScore { get; set; }
    [ProtoMember(10)] public ulong Action { get; set; }
    [ProtoMember(11)] public string ActionDescription { get; set; } = "";
    [ProtoMember(14)] public string PopStr { get; set; } = "";
}

[ProtoContract]
public class LikeMessage
{
    [ProtoMember(1)] public Common? Common { get; set; }
    [ProtoMember(2)] public ulong Count { get; set; }
    [ProtoMember(3)] public ulong Total { get; set; }
    [ProtoMember(5)] public User? User { get; set; }
}

[ProtoContract]
public class SocialMessage
{
    [ProtoMember(1)] public Common? Common { get; set; }
    [ProtoMember(2)] public User? User { get; set; }
    [ProtoMember(3)] public ulong ShareType { get; set; }
    [ProtoMember(4)] public ulong Action { get; set; }
}

[ProtoContract]
public class Text
{
    [ProtoMember(1)] public string Key { get; set; } = "";
    [ProtoMember(2)] public string DefaultPattern { get; set; } = "";
    [ProtoMember(3)] public TextFormat? DefaultFormat { get; set; }
    [ProtoMember(4)] public List<TextPiece> PiecesList { get; set; } = new();
}

[ProtoContract]
public class TextPiece
{
    [ProtoMember(1)] public int Type { get; set; }
    [ProtoMember(2)] public TextFormat? Format { get; set; }
    [ProtoMember(3)] public string StringValue { get; set; } = "";
    [ProtoMember(4)] public TextPieceUser? UserValue { get; set; }
    [ProtoMember(5)] public TextPieceGift? GiftValue { get; set; }
    [ProtoMember(7)] public TextPiecePatternRef? PatternRefValue { get; set; }
    [ProtoMember(8)] public TextPieceImage? ImageValue { get; set; }
}

[ProtoContract]
public class TextPieceImage
{
    [ProtoMember(1)] public Image? Image { get; set; }
    [ProtoMember(2)] public float ScalingRate { get; set; }
}

[ProtoContract]
public class TextPiecePatternRef
{
    [ProtoMember(1)] public string Key { get; set; } = "";
    [ProtoMember(2)] public string DefaultPattern { get; set; } = "";
}

[ProtoContract]
public class TextPieceUser
{
    [ProtoMember(1)] public User? User { get; set; }
    [ProtoMember(2)] public bool WithColon { get; set; }
}

[ProtoContract]
public class TextPieceGift
{
    [ProtoMember(1)] public ulong GiftId { get; set; }
    [ProtoMember(2)] public PatternRef? NameRef { get; set; }
}

[ProtoContract]
public class PatternRef
{
    [ProtoMember(1)] public string Key { get; set; } = "";
    [ProtoMember(2)] public string DefaultPattern { get; set; } = "";
}

[ProtoContract]
public class TextFormat
{
    [ProtoMember(1)] public string Color { get; set; } = "";
    [ProtoMember(2)] public bool Bold { get; set; }
    [ProtoMember(3)] public bool Italic { get; set; }
    [ProtoMember(4)] public uint Weight { get; set; }
    [ProtoMember(6)] public uint FontSize { get; set; }
    [ProtoMember(7)] public bool UseHeighLightColor { get; set; }
}
