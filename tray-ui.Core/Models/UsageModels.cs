using System;
using System.Collections.Generic;

namespace tray_ui.Models;

public enum ProviderKind
{
    Unknown = 0,
    Codex = 1
}

public sealed class UsageSummary
{
    public DateTimeOffset? LastUpdated { get; set; }
    public List<ProviderUsageSnapshot> ProviderSnapshots { get; init; } = new();
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
