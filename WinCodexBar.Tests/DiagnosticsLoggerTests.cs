using WinCodexBar.Core.Models;
using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class DiagnosticsLoggerTests
{
    [Fact]
    public void LogAttempt_AddsEntryAndRaisesEvent()
    {
        var logger = new DiagnosticsLogger();
        DiagnosticsLogEntry? raisedEntry = null;
        logger.EntryAdded += (_, entry) => raisedEntry = entry;

        logger.LogAttempt(ProviderKind.Codex, "oauth", "Trying OAuth");

        var storedEntry = Assert.Single(logger.GetEntries());
        Assert.Same(storedEntry, raisedEntry);
        Assert.Equal(ProviderKind.Codex, storedEntry.Provider);
        Assert.Equal(DiagnosticsEventType.FetchAttempt, storedEntry.EventType);
        Assert.Equal("oauth", storedEntry.SourceMethod);
        Assert.Equal("Trying OAuth", storedEntry.Message);
    }

    [Fact]
    public void LogAttempt_TrimsEntriesToMaxSize()
    {
        var logger = new DiagnosticsLogger();

        for (var index = 0; index < 105; index++)
        {
            logger.LogAttempt(ProviderKind.Codex, "oauth", $"message-{index}");
        }

        var entries = logger.GetEntries();
        Assert.Equal(100, entries.Count);
        Assert.Equal("message-5", entries[0].Message);
        Assert.Equal("message-104", entries[^1].Message);
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var logger = new DiagnosticsLogger();
        logger.LogRefreshStarted();
        logger.LogRefreshCompleted(TimeSpan.FromMilliseconds(25));

        logger.Clear();

        Assert.Empty(logger.GetEntries());
    }
}
