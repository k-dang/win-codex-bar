namespace WinCodexBar.Core.Models;

public enum DiagnosticsEventType
{
    FetchAttempt,
    FetchSuccess,
    FetchFailure,
    RefreshStarted,
    RefreshCompleted
}

// One source for the event label so the diagnostics table, the clipboard copy, and the
// log file cannot drift apart.
public static class DiagnosticsEventLabel
{
    public static string For(DiagnosticsEventType eventType)
    {
        return eventType switch
        {
            DiagnosticsEventType.FetchAttempt => "Attempt",
            DiagnosticsEventType.FetchSuccess => "Success",
            DiagnosticsEventType.FetchFailure => "Failure",
            DiagnosticsEventType.RefreshStarted => "Started",
            DiagnosticsEventType.RefreshCompleted => "Completed",
            _ => eventType.ToString()
        };
    }
}

public sealed record DiagnosticsLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public ProviderKind? Provider { get; init; }
    public DiagnosticsEventType EventType { get; init; }
    public string? SourceMethod { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public TimeSpan? Duration { get; init; }
}

