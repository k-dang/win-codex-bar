using System;
using System.Collections.Generic;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class DiagnosticsLogger : IDiagnosticsLogger
{
    private const int MaxEntries = 100;
    private readonly object _lock = new();
    private readonly LinkedList<DiagnosticsLogEntry> _entries = new();

    public event EventHandler<DiagnosticsLogEntry>? EntryAdded;

    public IReadOnlyList<DiagnosticsLogEntry> GetEntries()
    {
        lock (_lock)
        {
            return new List<DiagnosticsLogEntry>(_entries);
        }
    }

    public void LogAttempt(ProviderKind provider, string sourceMethod, string message)
    {
        var entry = new DiagnosticsLogEntry
        {
            Provider = provider,
            EventType = DiagnosticsEventType.FetchAttempt,
            SourceMethod = sourceMethod,
            Message = message
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

    public void LogFailure(ProviderKind provider, string sourceMethod, string message, TimeSpan duration)
    {
        var entry = new DiagnosticsLogEntry
        {
            Provider = provider,
            EventType = DiagnosticsEventType.FetchFailure,
            SourceMethod = sourceMethod,
            Message = message,
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
        lock (_lock)
        {
            _entries.AddLast(entry);
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveFirst();
            }
        }
        EntryAdded?.Invoke(this, entry);
    }
}
