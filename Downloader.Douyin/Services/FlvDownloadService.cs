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
        _cancelCts?.Dispose();
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
        _shutdownRequested = false;
        var shutdownStart = TimeSpan.Zero;

        try
        {
            while (true)
            {
                if (token.IsCancellationRequested && !_shutdownRequested)
                {
                    _shutdownRequested = true;
                    shutdownStart = stopwatch.Elapsed;
                    Console.Error.WriteLine("\n[下载] 正在等待可截断点后停止...");
                }

                if (currentSegment == null)
                {
                    segmentIndex++;
                    currentSegmentPath = Path.Combine(dir,
                        $"{baseName}_seg{segmentIndex:D3}.flv");
                    currentSegment = new FileStream(
                        currentSegmentPath, FileMode.Create, FileAccess.ReadWrite,
                        FileShare.Read, 81920, true);
                    segmentStartTime = stopwatch.Elapsed;
                    segmentFiles.Add(currentSegmentPath);

                    if (segmentIndex >= 2)
                    {
                        // 分段 2+：始终写入 FLV 头（含代码头或最小头），确保文件可被 ffmpeg 识别
                        byte[] segStart;
                        if (segmentPrefix != null)
                        {
                            var expectedPts = (segmentIndex - 1) * (long)segDuration.TotalMilliseconds;
                            segStart = AdjustCodecPts(segmentPrefix, expectedPts, expectedPts);
                        }
                        else
                        {
                            // 代码头未就绪时写入最小 FLV 头
                            segStart =
                            [
                                0x46, 0x4C, 0x56,       // "FLV"
                                0x01,                   // version 1
                                0x05,                   // flags: audio+video
                                0x00, 0x00, 0x00, 0x09, // header size = 9
                                0x00, 0x00, 0x00, 0x00  // PreviousTagSize0 = 0
                            ];
                        }
                        await currentSegment.WriteAsync(
                            segStart, 0, segStart.Length, CancellationToken.None);

                        // 从流中读取数据，扫描首个有效 FLV tag 边界（最多 256KB），
                        // 避免大视频帧跨段边界时写入裸流中间数据。
                        var initBuf = new byte[262144];
                        var totalInit = 0;
                        var foundTag = false;
                        var tagPos = -1;
                        while (!foundTag && totalInit < initBuf.Length)
                        {
                            var chunk = await responseStream.ReadAsync(
                                initBuf.AsMemory(totalInit, initBuf.Length - totalInit), token);
                            if (chunk == 0) break;
                            totalInit += chunk;

                            var p = 0;
                            while (p + 15 <= totalInit)
                            {
                                if (!IsValidTag(initBuf, p, totalInit)) { p++; continue; }
                                var ds = (initBuf[p + 1] << 16) | (initBuf[p + 2] << 8) | initBuf[p + 3];
                                var tagEnd = p + 11 + ds;
                                var nextP = tagEnd + 4;
                                if (nextP + 15 <= totalInit)
                                {
                                    if (!IsValidTag(initBuf, nextP, totalInit)) { p++; continue; }
                                }
                                else
                                {
                                    p++; continue;
                                }
                                tagPos = p;
                                foundTag = true;
                                break;
                            }
                        }
                        if (totalInit > 0)
                        {
                            // 未找到合法 tag 时舍弃此块数据（跨段残余），让主循环写后续干净数据
                            var writeFrom = foundTag ? tagPos : totalInit;
                            if (writeFrom < totalInit)
                                await currentSegment.WriteAsync(
                                    initBuf, writeFrom, totalInit - writeFrom, CancellationToken.None);
                        }
                    }
                }

                var bytesRead = 0;
                try
                {
                    bytesRead = await responseStream.ReadAsync(
                        buffer, 0, buffer.Length, token);
                }
                catch when (_shutdownRequested)
                {
                    break;
                }
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
                        await currentSegment.WriteAsync(buffer, 0, 13, CancellationToken.None);
                        var rest = bytesRead - tagEnd;
                        if (rest > 0)
                            await currentSegment.WriteAsync(buffer, tagEnd, rest, CancellationToken.None);

                        seg1Raw.AddRange(buffer.AsSpan(0, 13));
                        if (rest > 0)
                            seg1Raw.AddRange(buffer.AsSpan(tagEnd, rest));

                        bytesToLog = 13 + rest;
                        metadataSkipped = true;
                    }
                    else
                    {
                        await currentSegment.WriteAsync(buffer, 0, bytesRead, CancellationToken.None);
                        seg1Raw.AddRange(buffer.AsSpan(0, bytesRead));
                    }
                }
                else
                {
                    await currentSegment.WriteAsync(buffer, 0, bytesRead, CancellationToken.None);
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
                    // 段1未找到代码头（AVC/HEVC sequence header + AAC header）时暂不分段，
                    // 确保分段2+可独立解码。最长等待3倍 segDuration 后强制分。
                    if (segmentIndex == 1 && segmentPrefix == null && segmentElapsed < segDuration * 3)
                        continue;

                    var minSegBytes = segmentPrefix != null
                        ? segmentPrefix.Length + 4096 : 4096;
                    if (segmentIndex > 0 && currentSegment!.Length < minSegBytes)
                        continue;

                    await currentSegment.FlushAsync(CancellationToken.None);

                    // 截断至末个完整 FLV tag 边界，确保大帧跨段时前段尾部不损坏
                    if (currentSegment!.Length > 13)
                    {
                        var fileLen = currentSegment.Length;
                        var scanBytes = (int)Math.Min(fileLen, 262144);
                        var scanBuf = new byte[scanBytes];
                        currentSegment.Seek(-scanBytes, SeekOrigin.End);
                        _ = await currentSegment.ReadAsync(
                            scanBuf, 0, scanBytes, CancellationToken.None);
                        var boundary = FindLastTagBoundary(scanBuf, 0, scanBytes);
                        if (boundary >= 13 && boundary < scanBytes)
                            currentSegment.SetLength(fileLen - (scanBytes - boundary));
                    }

                    await currentSegment.DisposeAsync();
                    var completedPath = currentSegmentPath!;
                    var completedIndex = segmentIndex;
                    var fileSize = new FileInfo(completedPath).Length;

                    if (onSegmentCompleted != null)
                    {
                        try
                        {
                            await onSegmentCompleted(
                                completedIndex, completedPath, fileSize, CancellationToken.None);
                        }
                        catch
                        {
                        }
                    }

                    progressState.CurrentSegment = $"[{completedIndex}] 下载完成";
                    progress?.Report(progressState);

                    currentSegment = null;
                    currentSegmentPath = null;

                    if (_shutdownRequested) break;
                }
            }
        }
        finally
        {
            if (currentSegment != null)
            {
                await currentSegment.FlushAsync(CancellationToken.None);

                if (currentSegment.Length > 13)
                {
                    var fileLen = currentSegment.Length;
                    var scanBytes = (int)Math.Min(fileLen, 262144);
                    var scanBuf = new byte[scanBytes];
                    currentSegment.Seek(-scanBytes, SeekOrigin.End);
                    _ = await currentSegment.ReadAsync(
                        scanBuf, 0, scanBytes, CancellationToken.None);
                    var boundary = FindLastTagBoundary(scanBuf, 0, scanBytes);
                    if (boundary >= 13 && boundary < scanBytes)
                        currentSegment.SetLength(fileLen - (scanBytes - boundary));
                }

                await currentSegment.DisposeAsync();
                var finalPath = currentSegmentPath!;
                var fileSize = new FileInfo(finalPath).Length;

                // 最后一段始终加入转码队列（即使文件小），确保回放不丢失
                if (fileSize > 0 && onSegmentCompleted != null)
                {
                    try
                    {
                        await onSegmentCompleted(
                            segmentIndex, finalPath, fileSize, CancellationToken.None);
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
        _shutdownRequested = true;
        _cancelCts?.Cancel();
        _http.CancelPendingRequests();
    }

    private volatile bool _shutdownRequested;

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
        byte[]? hevcHeader = null;
        byte[]? aacHeader = null;

        while (pos + 11 <= raw.Count)
        {
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
                    else if (codecId == 12)
                    {
                        var hevcPacketType = tagData[12];
                        if (hevcPacketType == 0)
                            hevcHeader ??= tagData;
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

        if ((hasVideo && avcHeader == null && hevcHeader == null) ||
            (hasAudio && aacHeader == null))
            return false;

        using var ms = new MemoryStream();
        ms.Write(raw.GetRange(0, 9).ToArray());
        ms.Write([0, 0, 0, 0]);

        if (avcHeader != null)
        {
            ms.Write(avcHeader, 0, avcHeader.Length);
            WriteU32BE(ms, avcHeader.Length);
        }

        if (hevcHeader != null)
        {
            ms.Write(hevcHeader, 0, hevcHeader.Length);
            WriteU32BE(ms, hevcHeader.Length);
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

    /// <summary>
    /// 扫描字节范围，返回末个完整 FLV tag 结束位置（含 PreviousTagSize）的偏移，
    /// 未找到返回 -1。
    /// </summary>
    private static int FindLastTagBoundary(byte[] buffer, int offset, int count)
    {
        var end = offset + count;
        var last = -1;
        var pos = offset;

        while (pos + 15 <= end)
        {
            if (!IsValidTag(buffer, pos, end)) { pos++; continue; }

            var ds = (buffer[pos + 1] << 16) | (buffer[pos + 2] << 8) | buffer[pos + 3];
            var tagEnd = pos + 11 + ds;
            var ptsEnd = tagEnd + 4;
            if (ptsEnd > end) break;

            var ps = (buffer[tagEnd] << 24) | (buffer[tagEnd + 1] << 16) |
                     (buffer[tagEnd + 2] << 8) | buffer[tagEnd + 3];
            if (ps == 11 + ds)
            {
                last = ptsEnd;
                pos = ptsEnd;
            }
            else
            {
                pos++;
            }
        }

        return last;
    }

    /// <summary>校验 buf[pos] 是否为一个合法的 FLV tag 头：tag 类型 + streamID=0 + PreviousTagSize 匹配。</summary>
    private static bool IsValidTag(byte[] buf, int pos, int length)
    {
        if (pos + 15 > length) return false;
        var tt = buf[pos];
        if (tt != 8 && tt != 9 && tt != 18) return false;
        if (buf[pos + 8] != 0 || buf[pos + 9] != 0 || buf[pos + 10] != 0) return false;
        var ds = (buf[pos + 1] << 16) | (buf[pos + 2] << 8) | buf[pos + 3];
        var tagEnd = pos + 11 + ds;
        if (tagEnd + 4 > length) return false;
        var ps = (buf[tagEnd] << 24) | (buf[tagEnd + 1] << 16) |
                 (buf[tagEnd + 2] << 8) | buf[tagEnd + 3];
        return ps == 11 + ds;
    }

    /// <summary>扫描 buffer 找到第一个完整的 onMetaData 脚本标签（type 18），
    /// 返回该标签末尾（含 PreviousTagSize）的偏移。未找到返回 0。
    /// 通过 PreviousTagSize + 下一 tag 双校验防止误判。</summary>
    private static int SkipMetadataTag(byte[] buffer, int length)
    {
        var pos = 0;
        while (pos + 15 <= length)
        {
            if (!IsValidTag(buffer, pos, length)) { pos++; continue; }

            var dataSize = (buffer[pos + 1] << 16) | (buffer[pos + 2] << 8) | buffer[pos + 3];
            var tagEnd = pos + 11 + dataSize;
            var nextP = tagEnd + 4;

            if (nextP + 15 <= length)
            {
                // 双校验：下一个 tag 必须也合法
                if (!IsValidTag(buffer, nextP, length)) { pos++; continue; }
            }
            else
            {
                pos++; continue;
            }

            if (buffer[pos] == 18) return tagEnd + 4;
            pos = tagEnd + 4;
        }
        return 0;
    }
}
