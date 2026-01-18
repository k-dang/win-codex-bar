using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class LogScanner
{
    private readonly CacheStore _cacheStore;

    public LogScanner(CacheStore cacheStore)
    {
        _cacheStore = cacheStore;
    }

    public async Task<ScanResult> ScanAsync(AppSettings settings)
    {
        var result = new ScanResult();
        var cache = await _cacheStore.LoadAsync().ConfigureAwait(false);

        foreach (var root in settings.LogRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var entry = GetCacheEntry(cache, fileInfo);
                    var providerHint = InferProviderFromPath(file);

                    var records = await ScanFileAsync(fileInfo, entry, providerHint).ConfigureAwait(false);
                    result.FilesScanned += 1;
                    result.Summary.RecordsParsed += records.Count;
                    AddRecords(result.Summary, records);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{file}: {ex.Message}");
                }
            }
        }

        result.Summary.LastUpdated = DateTimeOffset.Now;
        await _cacheStore.SaveAsync(cache).ConfigureAwait(false);
        return result;
    }

    private static FileCacheEntry GetCacheEntry(ScanCache cache, FileInfo fileInfo)
    {
        if (!cache.Files.TryGetValue(fileInfo.FullName, out var entry))
        {
            entry = new FileCacheEntry
            {
                Path = fileInfo.FullName,
                Size = 0,
                Offset = 0,
                LastWriteUtcTicks = 0
            };
            cache.Files[fileInfo.FullName] = entry;
        }

        var lastWrite = fileInfo.LastWriteTimeUtc.Ticks;
        if (fileInfo.Length < entry.Offset || lastWrite < entry.LastWriteUtcTicks)
        {
            entry.Offset = 0;
            entry.IncompleteLineBase64 = null;
        }

        return entry;
    }

    private static async Task<List<UsageRecord>> ScanFileAsync(
        FileInfo fileInfo,
        FileCacheEntry entry,
        ProviderKind providerHint)
    {
        var records = new List<UsageRecord>();
        var offset = entry.Offset;
        var size = fileInfo.Length;
        var lastWrite = fileInfo.LastWriteTimeUtc.Ticks;

        if (offset == size && lastWrite == entry.LastWriteUtcTicks)
        {
            return records;
        }

        byte[]? pendingBytes = null;
        if (!string.IsNullOrWhiteSpace(entry.IncompleteLineBase64))
        {
            try
            {
                pendingBytes = Convert.FromBase64String(entry.IncompleteLineBase64);
            }
            catch
            {
                pendingBytes = null;
            }
        }

        using var stream = new FileStream(
            fileInfo.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        stream.Seek(offset, SeekOrigin.Begin);

        var buffer = new byte[64 * 1024];
        var lineBytes = new List<byte>(4096);

        if (pendingBytes is not null && pendingBytes.Length > 0)
        {
            lineBytes.AddRange(pendingBytes);
        }

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            for (var i = 0; i < bytesRead; i++)
            {
                var value = buffer[i];
                offset += 1;

                if (value == (byte)'\n')
                {
                    if (lineBytes.Count > 0 && lineBytes[^1] == (byte)'\r')
                    {
                        lineBytes.RemoveAt(lineBytes.Count - 1);
                    }

                    if (lineBytes.Count > 0)
                    {
                        var line = Encoding.UTF8.GetString(lineBytes.ToArray());
                        if (LogParser.TryParseLine(line, providerHint, fileInfo.FullName, out var record))
                        {
                            records.Add(record);
                        }
                    }

                    lineBytes.Clear();
                    continue;
                }

                lineBytes.Add(value);
            }
        }

        entry.Offset = offset;
        entry.Size = size;
        entry.LastWriteUtcTicks = lastWrite;
        entry.IncompleteLineBase64 = lineBytes.Count == 0
            ? null
            : Convert.ToBase64String(lineBytes.ToArray());

        return records;
    }

    private static void AddRecords(UsageSummary summary, List<UsageRecord> records)
    {
        foreach (var record in records)
        {
            var date = record.Timestamp.Date;
            var daily = summary.DailyTotals.Find(item => item.Date == date);
            if (daily == null)
            {
                daily = new DailyUsage { Date = date };
                summary.DailyTotals.Add(daily);
            }

            daily.InputTokens += record.InputTokens;
            daily.OutputTokens += record.OutputTokens;
            daily.TotalTokens += record.TotalTokens;

            if (summary.LastSession == null || record.Timestamp > summary.LastSession.LastActivity)
            {
                summary.LastSession = new SessionUsage
                {
                    Provider = record.Provider,
                    SessionId = record.SessionId,
                    LastActivity = record.Timestamp,
                    InputTokens = record.InputTokens,
                    OutputTokens = record.OutputTokens,
                    TotalTokens = record.TotalTokens
                };
                continue;
            }

            if (summary.LastSession.SessionId != null &&
                summary.LastSession.SessionId == record.SessionId &&
                record.Timestamp >= summary.LastSession.LastActivity)
            {
                summary.LastSession.LastActivity = record.Timestamp;
                summary.LastSession.InputTokens += record.InputTokens;
                summary.LastSession.OutputTokens += record.OutputTokens;
                summary.LastSession.TotalTokens += record.TotalTokens;
            }
        }
    }

    private static ProviderKind InferProviderFromPath(string path)
    {
        if (path.IndexOf("Claude", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ProviderKind.Claude;
        }

        if (path.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ProviderKind.Codex;
        }

        return ProviderKind.Unknown;
    }

}
