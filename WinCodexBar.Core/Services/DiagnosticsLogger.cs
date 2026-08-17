using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

public sealed class DiagnosticsLogger : IDiagnosticsLogger
{
    private const int MaxEntries = 100;
    private readonly object _lock = new();
    private readonly LinkedList<DiagnosticsLogEntry> _entries = new();
    private readonly DiagnosticsLogFile? _logFile;

    public DiagnosticsLogger(DiagnosticsLogFile? logFile = null)
    {
        _logFile = logFile;
    }

    public string? LogFilePath => _logFile?.FilePath;

    public event EventHandler<DiagnosticsLogEntry>? EntryAdded;

    public IReadOnlyList<DiagnosticsLogEntry> GetEntries()
    {
        lock (_lock)
        {
            return new List<DiagnosticsLogEntry>(_entries);
        }
    }

    public void LogAttempt(ProviderKind provider, string sourceMethod, string message, string? detail = null)
    {
        var entry = new DiagnosticsLogEntry
        {
            Provider = provider,
            EventType = DiagnosticsEventType.FetchAttempt,
            SourceMethod = sourceMethod,
            Message = message,
            Detail = detail
        };
        AddEntry(entry);
    }

    public void LogSuccess(ProviderKind provider, string sourceMethod, string message, TimeSpan duration)
    {
        var entry = new DiagnosticsLogEntry
        {
            Provider = provider,
            EventType = DiagnosticsEventType.FetchSuccess,
            SourceMethod = sourceMethod,
            Message = message,
            Duration = duration
        };
        AddEntry(entry);
    }

    public void LogFailure(ProviderKind provider, string sourceMethod, string message, TimeSpan duration, string? detail = null)
    {
        var entry = new DiagnosticsLogEntry
        {
            Provider = provider,
            EventType = DiagnosticsEventType.FetchFailure,
            SourceMethod = sourceMethod,
            Message = message,
            Detail = detail,
            Duration = duration
        };
        AddEntry(entry);
    }

    public void LogRefreshStarted()
    {
        var entry = new DiagnosticsLogEntry
        {
            EventType = DiagnosticsEventType.RefreshStarted,
            Message = "Refresh cycle started"
        };
        AddEntry(entry);
    }

    public void LogRefreshCompleted(TimeSpan duration)
    {
        var entry = new DiagnosticsLogEntry
        {
            EventType = DiagnosticsEventType.RefreshCompleted,
            Message = "Refresh cycle completed",
            Duration = duration
        };
        AddEntry(entry);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    private void AddEntry(DiagnosticsLogEntry entry)
    {
        // The file sink redacts on its way to disk, so it takes the entry as-is; this
        // copy is the one the UI binds to and puts on the clipboard.
        var redacted = entry with
        {
            Message = SecretRedactor.Redact(entry.Message),
            Detail = entry.Detail == null ? null : SecretRedactor.Redact(entry.Detail)
        };

        lock (_lock)
        {
            _entries.AddLast(redacted);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveFirst();
            }
        }

        _logFile?.Append(entry);
        EntryAdded?.Invoke(this, redacted);
    }
}
