using System.Text.Json;
using Recorder.Core.Models;

namespace Recorder.Core.Services;

public class ConfigWatcher : IDisposable
{
    private readonly string _configPath;
    private readonly FileSystemWatcher? _watcher;
    private readonly Timer _debounceTimer;
    private AppConfig _currentConfig;
    private readonly object _lock = new();
    private bool _dirty;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public event Action<AppConfig>? ConfigChanged;

    public ConfigWatcher(string configPath)
    {
        _configPath = configPath;
        _currentConfig = LoadConfig();

        var dir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(configPath))
            {
                NotifyFilter = NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnConfigFileChanged;
        }

        _debounceTimer = new Timer(_ => ReloadConfig(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public AppConfig GetConfig()
    {
        lock (_lock) return _currentConfig;
    }

    public void Reload()
    {
        _dirty = true;
        ReloadConfig();
    }

    private void OnConfigFileChanged(object? sender, FileSystemEventArgs e)
    {
        _dirty = true;
        _debounceTimer.Change(500, Timeout.Infinite);
    }

    private void ReloadConfig()
    {
        if (!_dirty) return;
        _dirty = false;

        try
        {
            var newConfig = LoadConfig();
            lock (_lock) _currentConfig = newConfig;
            ConfigChanged?.Invoke(newConfig);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[配置] 重载失败: {ex.Message}");
        }
    }

    private AppConfig LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            var defaults = new AppConfig();
            var json = JsonSerializer.Serialize(defaults, JsonOptions);
            File.WriteAllText(_configPath, json);
            return defaults;
        }

        for (var i = 0; i < 3; i++)
        {
            try
            {
                using var fs = new FileStream(_configPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return JsonSerializer.Deserialize<AppConfig>(fs, JsonOptions) ?? new AppConfig();
            }
            catch (IOException) when (i < 2)
            {
                Thread.Sleep(100);
            }
        }

        return GetConfig();
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer.Dispose();
    }
}
