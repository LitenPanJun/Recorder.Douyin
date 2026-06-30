namespace DouyinDanmaku.Utils;

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
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, length).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }
}
