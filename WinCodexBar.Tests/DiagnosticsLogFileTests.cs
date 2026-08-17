using WinCodexBar.Core.Models;
using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class DiagnosticsLogFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"wincodexbar-logs-{Guid.NewGuid():N}");

    [Fact]
    public void Append_WritesHeaderLineAndIndentedDetail()
    {
        var logFile = new DiagnosticsLogFile(_directory);

        logFile.Append(new DiagnosticsLogEntry
        {
            Provider = ProviderKind.Codex,
            EventType = DiagnosticsEventType.FetchFailure,
            SourceMethod = "OAuth",
            Message = "Codex OAuth API failed (401).",
            Detail = "url: https://chatgpt.com/backend-api/wham/usage\nstatus: 401 Unauthorized",
            Duration = TimeSpan.FromMilliseconds(120)
        });

        var lines = File.ReadAllLines(logFile.FilePath);
        Assert.Contains("FAILURE", lines[0]);
        Assert.Contains("Codex/OAuth (120ms) Codex OAuth API failed (401).", lines[0]);
        Assert.Equal("    url: https://chatgpt.com/backend-api/wham/usage", lines[1]);
        Assert.Equal("    status: 401 Unauthorized", lines[2]);
    }

    [Fact]
    public void Append_KeepsMessageOnASingleLine()
    {
        var logFile = new DiagnosticsLogFile(_directory);

        logFile.Append(new DiagnosticsLogEntry
        {
            EventType = DiagnosticsEventType.RefreshStarted,
            Message = "first\nsecond"
        });

        var lines = File.ReadAllLines(logFile.FilePath);
        Assert.Single(lines);
        Assert.EndsWith("first second", lines[0]);
    }

    [Fact]
    public void Append_RollsOverToPreviousFileWhenOversized()
    {
        var logFile = new DiagnosticsLogFile(_directory, maxBytes: 200);
        var entry = new DiagnosticsLogEntry
        {
            EventType = DiagnosticsEventType.RefreshCompleted,
            Message = new string('m', 100)
        };

        for (var index = 0; index < 5; index++)
        {
            logFile.Append(entry);
        }

        Assert.True(File.Exists(logFile.PreviousFilePath));
        Assert.True(new FileInfo(logFile.FilePath).Length < 200);
    }

    [Fact]
    public void Append_WhenDirectoryCannotBeCreated_DoesNotThrow()
    {
        // A file where the log directory should be makes CreateDirectory fail.
        var blockingPath = Path.Combine(Path.GetTempPath(), $"wincodexbar-blocked-{Guid.NewGuid():N}");
        File.WriteAllText(blockingPath, "not a directory");

        try
        {
            var logFile = new DiagnosticsLogFile(Path.Combine(blockingPath, "logs"));

            logFile.Append(new DiagnosticsLogEntry { Message = "dropped" });
        }
        finally
        {
            File.Delete(blockingPath);
        }
    }

    // The sink redacts on its own, so a caller that bypasses DiagnosticsLogger still
    // cannot write a secret to disk.
    [Fact]
    public void Append_RedactsWithoutRelyingOnTheCaller()
    {
        var logFile = new DiagnosticsLogFile(_directory);

        logFile.Append(new DiagnosticsLogEntry
        {
            EventType = DiagnosticsEventType.FetchFailure,
            Message = "authorization: Bearer sk-live-abc123def456",
            Detail = "body: {\"refresh_token\":\"super-secret\"}"
        });

        var contents = File.ReadAllText(logFile.FilePath);
        Assert.DoesNotContain("sk-live-abc123def456", contents);
        Assert.DoesNotContain("super-secret", contents);
        Assert.Contains("Bearer ***", contents);
    }

    [Fact]
    public void Logger_WithLogFile_PersistsRedactedEntries()
    {
        var logFile = new DiagnosticsLogFile(_directory);
        var logger = new DiagnosticsLogger(logFile);

        logger.LogFailure(
            ProviderKind.Claude,
            "OAuth",
            "Claude OAuth API failed (401).",
            TimeSpan.FromMilliseconds(50),
            "body: {\"access_token\":\"super-secret\"}");

        var contents = File.ReadAllText(logFile.FilePath);
        Assert.Equal(logFile.FilePath, logger.LogFilePath);
        Assert.Contains("Claude OAuth API failed (401).", contents);
        Assert.DoesNotContain("super-secret", contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
