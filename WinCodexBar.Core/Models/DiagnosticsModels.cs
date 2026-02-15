using System;

namespace WinCodexBar.Core.Models;

public enum DiagnosticsEventType
{
    FetchAttempt,
    FetchSuccess,
    FetchFailure,
    RefreshStarted,
    RefreshCompleted
}

public sealed record DiagnosticsLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public ProviderKind? Provider { get; init; }
    public DiagnosticsEventType EventType { get; init; }
    public string? SourceMethod { get; init; }
    public string Message { get; init; } = string.Empty;
    public TimeSpan? Duration { get; init; }
}

