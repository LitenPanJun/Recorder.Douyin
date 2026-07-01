namespace Recorder.Shared;

public static class Log
{
    public static void Info(string message) => Console.Error.WriteLine($"[INFO] {message}");
    public static void Warn(string message) => Console.Error.WriteLine($"[WARN] {message}");
    public static void Error(string message) => Console.Error.WriteLine(message);
    public static void Error(Exception ex) => Console.Error.WriteLine($"[EXCEPTION] {ex}");
}
