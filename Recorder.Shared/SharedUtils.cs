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
}
