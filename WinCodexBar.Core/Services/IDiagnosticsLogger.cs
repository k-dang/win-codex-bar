using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

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


