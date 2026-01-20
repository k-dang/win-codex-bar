using System;
using tray_ui.Models;

namespace tray_ui.Services;

public interface IDiagnosticsLogger
{
    void LogAttempt(ProviderKind provider, string sourceMethod, string message);
    void LogSuccess(ProviderKind provider, string sourceMethod, string message, TimeSpan duration);
    void LogFailure(ProviderKind provider, string sourceMethod, string message, TimeSpan duration);
    void LogRefreshStarted();
    void LogRefreshCompleted(TimeSpan duration);
    void Clear();

    event EventHandler<DiagnosticsLogEntry>? EntryAdded;
}
