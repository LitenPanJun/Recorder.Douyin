using System.Net.WebSockets;

namespace DouyinDanmaku.Services;

public enum WebSocketStatus
{
    Closed,
    Connected,
    Failed
}

public class WebSocketClient : IDisposable
{
    private readonly string _url;
    private readonly string? _backupUrl;
    private readonly int _heartbeatIntervalMs;
    private readonly Dictionary<string, string> _headers;
    private readonly TimeSpan _connectTimeout = TimeSpan.FromSeconds(15);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private Timer? _heartbeatTimer;
    private Timer? _reconnectTimer;
    private int _reconnectCount;
    private const int MaxReconnect = 5;
    private const int ReconnectIntervalMs = 5000;
    private volatile bool _disposed;
    private volatile bool _stopping;
    private int _connecting;
    private bool _useBackup;

    public WebSocketStatus Status { get; private set; } = WebSocketStatus.Closed;

    public event Action<byte[]>? OnMessage;
    public event Action<string>? OnClose;
    public event Action? OnReconnect;
    public event Action? OnReady;
    public event Action? OnHeartbeat;

    public WebSocketClient(
        string url,
        string? backupUrl = null,
        int heartbeatIntervalMs = 10000,
        Dictionary<string, string>? headers = null)
    {
        _url = url;
        _backupUrl = backupUrl;
        _heartbeatIntervalMs = heartbeatIntervalMs;
        _headers = headers ?? new();
    }

    public async Task ConnectAsync()
    {
        if (_stopping || _disposed) return;
        if (Interlocked.CompareExchange(ref _connecting, 1, 0) != 0)
            return;

        _stopping = false;
        _useBackup = false;
        CleanupResources(disposeCts: true);
        _cts = new CancellationTokenSource();

        try
        {
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            foreach (var (key, value) in _headers)
                _ws.Options.SetRequestHeader(key, value);

            var targetUrl = _url;

            using var timeoutCts = new CancellationTokenSource(_connectTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

            try
            {
                await _ws.ConnectAsync(new Uri(targetUrl), linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !_cts.Token.IsCancellationRequested)
            {
                OnError("连接超时");
                CleanupResources(disposeCts: true);
                Interlocked.Exchange(ref _connecting, 0);
                return;
            }

            HandleReady();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            if (_stopping)
            {
                CleanupResources(disposeCts: false);
                Interlocked.Exchange(ref _connecting, 0);
                return;
            }
            if (!_useBackup && !string.IsNullOrEmpty(_backupUrl))
            {
                _useBackup = true;
                CleanupResources(disposeCts: true);
                Interlocked.Exchange(ref _connecting, 0);
                await ConnectAsync();
                return;
            }
            OnError("连接被取消");
            CleanupResources(disposeCts: false);
            Interlocked.Exchange(ref _connecting, 0);
            return;
        }
        catch (Exception ex)
        {
            if (!_useBackup && !string.IsNullOrEmpty(_backupUrl) && !_stopping)
            {
                _useBackup = true;
                CleanupResources(disposeCts: true);
                Interlocked.Exchange(ref _connecting, 0);
                await ConnectAsync();
                return;
            }
            OnError($"连接失败:{ex.Message}");
            CleanupResources(disposeCts: false);
            Interlocked.Exchange(ref _connecting, 0);
            return;
        }

        Status = WebSocketStatus.Connected;
        StartHeartbeat();
        _reconnectCount = 0;
        Interlocked.Exchange(ref _connecting, 0);
        _ = ReceiveLoopAsync();
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        try
        {
            while (_ws?.State == WebSocketState.Open && !_stopping)
            {
                var result = await _ws.ReceiveAsync(
                    new ArraySegment<byte>(buffer), CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    HandleConnectionLost(false);
                    return;
                }

                ms.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    var data = ms.ToArray();
                    _reconnectCount = 0;
                    OnMessage?.Invoke(data);
                    ms.SetLength(0);
                }
            }
        }
        catch (WebSocketException)
        {
            HandleConnectionLost(false);
            return;
        }
        catch (OperationCanceledException)
        {
            HandleConnectionLost(false);
            return;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (Exception ex)
        {
            OnError(ex.Message);
            return;
        }
    }

    private void HandleConnectionLost(bool intentional)
    {
        if (_stopping || _disposed) return;

        CleanupResources(disposeCts: false);

        if (intentional)
            return;

        if (_reconnectCount >= MaxReconnect)
        {
            OnClose?.Invoke("重连超过最大次数，与服务器断开连接");
            return;
        }

        _reconnectCount++;
        OnReconnect?.Invoke();

        _reconnectTimer = new Timer(async _ =>
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            if (!_stopping && !_disposed)
                await ConnectAsync();
        }, null, ReconnectIntervalMs, Timeout.Infinite);
    }

    public void Send(byte[] data)
    {
        try
        {
            _ws?.SendAsync(new ArraySegment<byte>(data),
                WebSocketMessageType.Binary, true,
                CancellationToken.None);
        }
        catch { }
    }

    public void Close()
    {
        _stopping = true;
        CleanupResources(disposeCts: false);
    }

    private void HandleReady()
    {
        Status = WebSocketStatus.Connected;
        OnReady?.Invoke();
    }

    private void OnError(string msg)
    {
        Status = WebSocketStatus.Failed;
        OnClose?.Invoke(msg);
    }

    private void CleanupResources(bool disposeCts)
    {
        Status = WebSocketStatus.Closed;

        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        _reconnectTimer?.Dispose();
        _reconnectTimer = null;

        if (disposeCts)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        else
        {
            _cts?.Cancel();
        }

        if (_ws != null)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait(1000); } catch { }
            _ws.Dispose();
            _ws = null;
        }
    }

    private void StartHeartbeat()
    {
        _heartbeatTimer = new Timer(
            _ => OnHeartbeat?.Invoke(),
            null, _heartbeatIntervalMs, _heartbeatIntervalMs);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopping = true;
        CleanupResources(disposeCts: true);
        GC.SuppressFinalize(this);
    }
}
