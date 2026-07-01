namespace API.Douyin.Models;

public class CaptchaRequiredException : Exception
{
    public string Url { get; }

    public CaptchaRequiredException(string url)
        : base($"需要验证码: {url}")
    {
        Url = url;
    }
}
