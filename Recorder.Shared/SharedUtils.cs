using System.Security.Cryptography;

namespace Recorder.Shared;

public static class SharedUtils
{
    public static string GenerateRandomId(int length)
    {
        var chars = new char[length];
        chars[0] = (char)('1' + Random.Shared.Next(0, 9));
        for (int i = 1; i < length; i++)
            chars[i] = (char)('0' + Random.Shared.Next(0, 10));
        return new string(chars);
    }

    public static string GenerateMsToken(int length = 128)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var data = RandomNumberGenerator.GetBytes(length);
        var result = new char[length];
        for (var i = 0; i < length; i++)
            result[i] = chars[data[i] % chars.Length];
        return new string(result);
    }

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }
}
