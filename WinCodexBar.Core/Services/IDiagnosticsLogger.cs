using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

public interface IDiagnosticsLogger
{
    string? LogFilePath { get; }

    void LogAttempt(ProviderKind provider, string sourceMethod, string message, string? detail = null);
    void LogSuccess(ProviderKind provider, string sourceMethod, string message, TimeSpan duration);
    void LogFailure(ProviderKind provider, string sourceMethod, string message, TimeSpan duration, string? detail = null);
    void LogRefreshStarted();
    void LogRefreshCompleted(TimeSpan duration);
    void Clear();

    event EventHandler<DiagnosticsLogEntry>? EntryAdded;
}
