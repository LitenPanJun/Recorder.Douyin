using System.Collections.Concurrent;
using API.Douyin;
using API.Douyin.Models;
using Recorder.Core.Models;

namespace Recorder.Core.Services;

public class PollCoordinator
{
    private readonly ConcurrentDictionary<string, StreamerRecorder> _recorders;
    private readonly ConfigWatcher _configWatcher;

    private const int GroupSize = 3;
    private static readonly TimeSpan StreamerInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan GroupInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RoundInterval = TimeSpan.FromSeconds(30);

    public PollCoordinator(ConcurrentDictionary<string, StreamerRecorder> recorders, ConfigWatcher configWatcher)
    {
        _recorders = recorders;
        _configWatcher = configWatcher;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var roundStart = DateTime.UtcNow;
            var snapshot = _recorders.Values.ToArray();

            for (var i = 0; i < snapshot.Length; i += GroupSize)
            {
                if (i > 0)
                {
                    try { await Task.Delay(GroupInterval, ct); }
                    catch (OperationCanceledException) { return; }
                }

                var group = snapshot.Skip(i).Take(GroupSize).ToList();
                for (var j = 0; j < group.Count; j++)
                {
                    if (j > 0)
                    {
                        try { await Task.Delay(StreamerInterval, ct); }
                        catch (OperationCanceledException) { return; }
                    }

                    var recorder = group[j];
                    if (recorder.IsRecording)
                        continue;

                    LiveRoomDetail? detail;
                    try { detail = await recorder.PollAsync(ct); }
                    catch (OperationCanceledException) { return; }
                    catch { detail = null; }

                    if (detail == null)
                        continue;

                    var resolvedRoomId = detail.DanmakuData?.RoomId;
                    if (!string.IsNullOrEmpty(resolvedRoomId) && resolvedRoomId.Length > 16 && resolvedRoomId.All(char.IsDigit))
                    {
                        _configWatcher.UpdateStreamerRoomId(recorder.StreamerId, resolvedRoomId);
                        recorder.SetRoomId(resolvedRoomId);
                    }

                    recorder.StartRecording(detail);
                }
            }

            var elapsed = DateTime.UtcNow - roundStart;
            var remaining = RoundInterval - elapsed;
            if (remaining > TimeSpan.Zero)
            {
                try { await Task.Delay(remaining, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
