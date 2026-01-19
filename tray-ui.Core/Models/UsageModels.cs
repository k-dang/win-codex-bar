using System;
using System.Collections.Generic;

namespace tray_ui.Models;

public enum ProviderKind
{
    Unknown = 0,
    Codex = 1
}

public sealed class UsageRecord
{
    public ProviderKind Provider { get; init; } = ProviderKind.Unknown;
    public string? SessionId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int TotalTokens { get; init; }
    public string SourceFile { get; init; } = string.Empty;
}

public sealed class DailyUsage
{
    public DateTime Date { get; init; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
}

public sealed class SessionUsage
{
    public ProviderKind Provider { get; init; } = ProviderKind.Unknown;
    public string? SessionId { get; init; }
    public DateTimeOffset LastActivity { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
}

public sealed class UsageSummary
{
    public List<DailyUsage> DailyTotals { get; init; } = new();
    public SessionUsage? LastSession { get; set; }
    public DateTimeOffset? LastUpdated { get; set; }
    public int RecordsParsed { get; set; }
    public List<ProviderUsageSnapshot> ProviderSnapshots { get; init; } = new();
    public List<string> LogRoots { get; init; } = new();
    public List<string> ScanErrors { get; init; } = new();
}

public sealed class ScanResult
{
    public UsageSummary Summary { get; init; } = new();
    public int FilesScanned { get; set; }
    public List<string> Errors { get; } = new();
}

public sealed class UsageWindow
{
    public string Label { get; init; } = string.Empty;
    public double? UsedPercent { get; init; }
    public int? WindowMinutes { get; init; }
    public DateTimeOffset? ResetsAt { get; init; }
    public string? ResetDescription { get; init; }
}

public sealed class ProviderUsageSnapshot
{
    public ProviderKind Provider { get; init; } = ProviderKind.Unknown;
    public string SourceLabel { get; init; } = "unknown";
    public string? AccountEmail { get; init; }
    public string? AccountPlan { get; init; }
    public UsageWindow? Primary { get; init; }
    public UsageWindow? Secondary { get; init; }
    public string? CreditsText { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.Now;
}
