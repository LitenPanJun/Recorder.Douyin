using System.Diagnostics;
using System.Net;
using Downloader.Douyin.Models;
using Recorder.Shared;

namespace Downloader.Douyin.Services;

public class FlvDownloadService
{
    private readonly HttpClient _http;
    private CancellationTokenSource? _cancelCts;

    public FlvDownloadService()
    {
        _http = HttpUtils.CreateClient(TimeSpan.FromHours(4), DecompressionMethods.None);
    }

    public async Task<DownloadResult> DownloadAsync(
        string url,
        string basePath,
        TimeSpan segmentDuration,
        Func<int, string, long, CancellationToken, Task>? onSegmentCompleted = null,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        _cancelCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _cancelCts.Token;

        var segDuration = segmentDuration <= TimeSpan.Zero
            ? TimeSpan.FromMinutes(10)
            : segmentDuration;
        var baseName = Path.GetFileName(basePath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? ".";
        Directory.CreateDirectory(dir);

        var stopwatch = Stopwatch.StartNew();
        var progressState = new DownloadProgress();
        long lastBytes = 0;
        var segmentIndex = 0;
        var totalDownloaded = 0L;
        var segmentFiles = new List<string>();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        request.Headers.Add("Referer", "https://live.douyin.com");

        using var response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        progressState.TotalBytes = response.Content.Headers.ContentLength;
        await using var responseStream = await response.Content.ReadAsStreamAsync(token);

        var buffer = new byte[81920];
        var segmentStartTime = stopwatch.Elapsed;
        FileStream? currentSegment = null;
        string? currentSegmentPath = null;

        byte[]? segmentPrefix = null;
        var seg1Raw = new List<byte>(65536);
        var metadataSkipped = false;

        try
        {
            while (!token.IsCancellationRequested)
            {
                if (currentSegment == null)
                {
                    segmentIndex++;
                    currentSegmentPath = Path.Combine(dir,
                        $"{baseName}_seg{segmentIndex:D3}.flv");
                    currentSegment = new FileStream(
                        currentSegmentPath, FileMode.Create, FileAccess.Write,
                        FileShare.Read, 81920, true);
                    segmentStartTime = stopwatch.Elapsed;
                    segmentFiles.Add(currentSegmentPath);

                    if (segmentIndex == 1)
                    {
                        // Segment 1: nothing extra to write before the main loop data
                    }
                    else if (segmentPrefix != null)
                    {
                        var expectedPts = (segmentIndex - 1) * (long)segDuration.TotalMilliseconds;
                        segmentPrefix = AdjustCodecPts(segmentPrefix, expectedPts, expectedPts);
                        await currentSegment.WriteAsync(
                            segmentPrefix, 0, segmentPrefix.Length, token);

                        var initBuf = new byte[81920];
                        var initRead = await responseStream.ReadAsync(
                            initBuf.AsMemory(0, initBuf.Length), token);
                        if (initRead > 0)
                        {
                            int firstTagStart = -1;
                            var p = 0;
                            while (p + 11 <= initRead)
                            {
                                var t = initBuf[p];
                                if (t != 8 && t != 9 && t != 18) { p++; continue; }
                                if (initBuf[p + 8] != 0 || initBuf[p + 9] != 0 || initBuf[p + 10] != 0)
                                { p++; continue; }
                                var ds = (initBuf[p + 1] << 16) |
                                         (initBuf[p + 2] << 8) | initBuf[p + 3];
                                if (p + 11 + ds > initRead) break;
                                firstTagStart = p;
                                break;
                            }
                            var writeFrom = firstTagStart >= 0 ? firstTagStart : 0;
                            await currentSegment.WriteAsync(
                                initBuf, writeFrom, initRead - writeFrom, token);
                        }
                    }
                }

                var bytesRead = await responseStream.ReadAsync(
                    buffer, 0, buffer.Length, token);
                if (bytesRead == 0) break;

                var bytesToLog = bytesRead;

                if (segmentIndex == 1 && !metadataSkipped)
                {
                    var tagEnd = SkipMetadataTag(buffer, bytesRead);
                    if (tagEnd > 13)
                    {
                        // buffer[0..13] = FLV header + PreviousTagSize (keep)
                        // buffer[13..tagEnd] = onMetaData tag (skip)
                        // buffer[tagEnd..] = remaining data (keep)
                        await currentSegment.WriteAsync(buffer, 0, 13, token);
                        var rest = bytesRead - tagEnd;
                        if (rest > 0)
                            await currentSegment.WriteAsync(buffer, tagEnd, rest, token);

                        seg1Raw.AddRange(buffer.AsSpan(0, 13));
                        if (rest > 0)
                            seg1Raw.AddRange(buffer.AsSpan(tagEnd, rest));

                        bytesToLog = 13 + rest;
                        metadataSkipped = true;
                    }
                    else
                    {
                        await currentSegment.WriteAsync(buffer, 0, bytesRead, token);
                        seg1Raw.AddRange(buffer.AsSpan(0, bytesRead));
                    }
                }
                else
                {
                    await currentSegment.WriteAsync(buffer, 0, bytesRead, token);
                    if (segmentIndex == 1 && segmentPrefix == null)
                        seg1Raw.AddRange(buffer.AsSpan(0, bytesRead));
                }

                totalDownloaded += bytesToLog;

                if (segmentIndex == 1 && segmentPrefix == null)
                {
                    if (TryExtractFlvPrefix(seg1Raw, out var prefix))
                        segmentPrefix = prefix;
                }

                var segmentElapsed = stopwatch.Elapsed - segmentStartTime;
                progressState.BytesDownloaded = totalDownloaded;
                progressState.Elapsed = stopwatch.Elapsed;
                progressState.CurrentSegment = $"[{segmentIndex}] 下载中...";
                progressState.SegmentsCompleted = segmentIndex - 1;

                if (progress != null && stopwatch.ElapsedMilliseconds % 500 < 100)
                {
                    var speed = (totalDownloaded - lastBytes) / 0.5;
                    progressState.SpeedBytesPerSecond = Math.Max(0, speed);
                    lastBytes = totalDownloaded;
                    progress.Report(progressState);
                }

                if (segmentElapsed >= segDuration)
                {
                    var minSegBytes = segmentPrefix != null
                        ? segmentPrefix.Length + 4096 : 4096;
                    if (segmentIndex > 0 && currentSegment!.Length < minSegBytes)
                        continue;

                    await currentSegment.FlushAsync(token);
                    await currentSegment.DisposeAsync();
                    var completedPath = currentSegmentPath!;
                    var completedIndex = segmentIndex;
                    var fileSize = new FileInfo(completedPath).Length;

                    if (onSegmentCompleted != null)
                    {
                        try
                        {
                            await onSegmentCompleted(
                                completedIndex, completedPath, fileSize, token);
                        }
                        catch
                        {
                        }
                    }

                    progressState.CurrentSegment = $"[{completedIndex}] 下载完成";
                    progress?.Report(progressState);

                    currentSegment = null;
                    currentSegmentPath = null;
                }
            }
        }
        finally
        {
            if (currentSegment != null)
            {
                await currentSegment.FlushAsync(token);
                await currentSegment.DisposeAsync();
                var finalPath = currentSegmentPath!;
                var fileSize = new FileInfo(finalPath).Length;

                if (segmentIndex > 1 && segmentPrefix != null
                    && fileSize < segmentPrefix.Length + 4096)
                {
                    try { File.Delete(finalPath); } catch { }
                    segmentFiles.Remove(finalPath);
                    Console.Error.WriteLine(
                        $"[FlvDownload] 分段 {segmentIndex} 数据过小 ({fileSize}B)，已删除");
                }
                else if (fileSize > 0 && onSegmentCompleted != null)
                {
                    try
                    {
                        await onSegmentCompleted(
                            segmentIndex, finalPath, fileSize, token);
                    }
                    catch
                    {
                    }
                }
            }
        }

        progressState.IsCompleted = true;
        progressState.Elapsed = stopwatch.Elapsed;
        progressState.CurrentSegment = "下载完成";
        progress?.Report(progressState);

        return new DownloadResult
        {
            TotalBytes = totalDownloaded,
            Elapsed = stopwatch.Elapsed,
            SegmentCount = segmentIndex,
            OutputDirectory = dir,
            BaseFileName = baseName,
            SegmentFiles = segmentFiles
        };
    }

    public void Cancel()
    {
        _cancelCts?.Cancel();
        _http.CancelPendingRequests();
    }

    private static bool TryExtractFlvPrefix(List<byte> raw, out byte[] prefix)
    {
        prefix = [];
        if (raw.Count < 13) return false;
        if (raw[0] != 'F' || raw[1] != 'L' || raw[2] != 'V') return false;

        var flvFlags = raw[4];
        var hasAudio = (flvFlags & 1) != 0;
        var hasVideo = (flvFlags & 4) != 0;

        var pos = 13;
        byte[]? avcHeader = null;
        byte[]? aacHeader = null;

        while (pos + 11 <= raw.Count)
        {
            var tagStart = pos;
            var tagType = raw[pos];
            var dataSize = (raw[pos + 1] << 16) | (raw[pos + 2] << 8) | raw[pos + 3];
            var tagLen = 11 + dataSize;
            if (pos + tagLen > raw.Count) break;

            var tagData = raw.GetRange(pos, tagLen).ToArray();

            if (tagType == 9 && hasVideo)
            {
                if (tagData.Length >= 12)
                {
                    var codecId = tagData[11] & 0x0F;
                    if (codecId == 7)
                    {
                        var avcPacketType = tagData[12];
                        if (avcPacketType == 0)
                            avcHeader ??= tagData;
                    }
                }
            }
            else if (tagType == 8 && hasAudio)
            {
                if (tagData.Length >= 12)
                {
                    var audioFormat = tagData[11] >> 4;
                    if (audioFormat == 10)
                    {
                        var aacPacketType = tagData[12];
                        if (aacPacketType == 0)
                            aacHeader ??= tagData;
                    }
                }
            }

            pos += tagLen + 4;
        }

        if (avcHeader == null && (!hasVideo || aacHeader == null))
            return false;

        using var ms = new MemoryStream();
        ms.Write(raw.GetRange(0, 9).ToArray());
        ms.Write([0, 0, 0, 0]);

        if (avcHeader != null)
        {
            ms.Write(avcHeader, 0, avcHeader.Length);
            WriteU32BE(ms, avcHeader.Length);
        }

        if (aacHeader != null)
        {
            ms.Write(aacHeader, 0, aacHeader.Length);
            WriteU32BE(ms, aacHeader.Length);
        }

        prefix = ms.ToArray();
        return true;
    }

    private static void WriteU32BE(MemoryStream ms, int value)
    {
        ms.WriteByte((byte)(value >> 24));
        ms.WriteByte((byte)(value >> 16));
        ms.WriteByte((byte)(value >> 8));
        ms.WriteByte((byte)(value));
    }

    private static byte[] AdjustCodecPts(byte[] prefix, long videoPts, long audioPts)
    {
        var result = prefix.ToArray();
        var pos = 13;

        if (pos + 11 <= result.Length && result[pos] == 9 && videoPts >= 0)
        {
            var pts = (uint)videoPts;
            result[pos + 4] = (byte)(pts >> 16);
            result[pos + 5] = (byte)(pts >> 8);
            result[pos + 6] = (byte)(pts);
            result[pos + 7] = (byte)(pts >> 24);
            var tagLen = 11 + ((result[pos + 1] << 16) | (result[pos + 2] << 8) | result[pos + 3]);
            pos += tagLen + 4;
        }

        if (pos + 11 <= result.Length && result[pos] == 8 && audioPts >= 0)
        {
            var pts = (uint)audioPts;
            result[pos + 4] = (byte)(pts >> 16);
            result[pos + 5] = (byte)(pts >> 8);
            result[pos + 6] = (byte)(pts);
            result[pos + 7] = (byte)(pts >> 24);
        }

        return result;
    }

    /// <summary>扫描 buffer 找到第一个完整的 onMetaData 脚本标签（type 18），
    /// 返回该标签末尾（含 PreviousTagSize）的偏移。未找到返回 0。</summary>
    private static int SkipMetadataTag(byte[] buffer, int length)
    {
        var pos = 0;
        while (pos + 11 <= length)
        {
            var tagType = buffer[pos];
            if (tagType != 8 && tagType != 9 && tagType != 18) { pos++; continue; }
            if (buffer[pos + 8] != 0 || buffer[pos + 9] != 0 || buffer[pos + 10] != 0)
            { pos++; continue; }

            var dataSize = (buffer[pos + 1] << 16) | (buffer[pos + 2] << 8) | buffer[pos + 3];
            var tagEnd = pos + 11 + dataSize + 4;
            if (tagEnd > length) break;

            if (tagType == 18) return tagEnd;
            pos = tagEnd;
        }
        return 0;
    }
}
