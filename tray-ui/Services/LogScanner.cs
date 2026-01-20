using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class LogScanner
{
    public LogScanner()
    {
    }

    public async Task<ScanResult> ScanAsync(AppSettings settings)
    {
        var result = new ScanResult();

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
                    var providerHint = InferProviderFromPath(file);

                    var records = await ScanFileAsync(fileInfo, providerHint).ConfigureAwait(false);
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
        return result;
    }

    private static async Task<List<UsageRecord>> ScanFileAsync(
        FileInfo fileInfo,
        ProviderKind providerHint)
    {
        var records = new List<UsageRecord>();

        using var stream = new FileStream(
            fileInfo.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024, leaveOpen: false);
        while (true)
        {
            var line = await reader.ReadLineAsync().ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            if (LogParser.TryParseLine(line, providerHint, fileInfo.FullName, out var record))
            {
                records.Add(record);
            }
        }

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
        if (path.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ProviderKind.Codex;
        }

        return ProviderKind.Unknown;
    }

}
