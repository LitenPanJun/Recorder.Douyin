using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Jint;

namespace DouyinDanmaku.Services;

public class DouyinSign
{
    private static string? _mssdkJs;
    private static readonly object _lock = new();

    private static string LoadEmbeddedJs(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Matches Dart getMsStub(roomId, uniqueId)
    public static string GetMsStub(string roomId, string userId)
    {
        var parts = new[]
        {
            "live_id=1",
            "aid=6383",
            "version_code=180800",
            "webcast_sdk_version=1.3.0",
            $"room_id={roomId}",
            "sub_room_id=",
            "sub_channel_id=",
            "did_rule=3",
            $"user_unique_id={userId}",
            "device_platform=web",
            "device_type=",
            "ac=",
            "identity=audience",
        };

        var sigParams = string.Join(",", parts);
        var bytes = Encoding.UTF8.GetBytes(sigParams);
        var hash = MD5.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Matches Dart getMSSDKSignature(roomId, userId, userAgent)
    // In Dart: var msStub = getMsStub(roomId, uniqueId);
    //           flutterJs.eval("getMSSDKSignature('$msStub','$defaultUserAgent')")
    public static string GetMSSDKSignature(string roomId, string userId, string userAgent)
    {
        lock (_lock)
        {
            _mssdkJs ??= LoadEmbeddedJs("DouyinDanmaku.Services.mssdk.js");
        }

        var msStub = GetMsStub(roomId, userId);

        var engine = new Engine(options =>
        {
            options.TimeoutInterval(TimeSpan.FromSeconds(30));
            options.MaxStatements(5_000_000);
            options.LimitMemory(200_000_000);
        });

        engine.Execute(_mssdkJs);

        var signature = engine.Invoke("getMSSDKSignature", msStub, userAgent).AsString();

        // Matches Dart: regenerate if signature contains - or =
        while (signature.Contains('-') || signature.Contains('='))
        {
            signature = engine.Invoke("getMSSDKSignature", msStub, userAgent).AsString();
        }

        return signature;
    }
}
