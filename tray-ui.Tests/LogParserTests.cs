using System;
using tray_ui.Models;
using tray_ui.Services;
using Xunit;

namespace tray_ui.Tests;

public class LogParserTests
{
    [Fact]
    public void TryParseLine_ParsesUsageAndProvider()
    {
        var line = "{\"timestamp\":\"2025-01-01T00:00:00Z\",\"model\":\"claude-3-5\",\"usage\":{\"input_tokens\":5,\"output_tokens\":7}}";

        var ok = LogParser.TryParseLine(line, ProviderKind.Unknown, "source.jsonl", out var record);

        Assert.True(ok);
        Assert.Equal(ProviderKind.Claude, record.Provider);
        Assert.Equal(5, record.InputTokens);
        Assert.Equal(7, record.OutputTokens);
        Assert.Equal(12, record.TotalTokens);
        Assert.Equal(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), record.Timestamp);
    }

    [Fact]
    public void TryParseLine_ReturnsFalseWhenUsageMissing()
    {
        var line = "{\"timestamp\":\"2025-01-01T00:00:00Z\",\"model\":\"gpt-4\"}";

        var ok = LogParser.TryParseLine(line, ProviderKind.Unknown, "source.jsonl", out _);

        Assert.False(ok);
    }

    [Fact]
    public void TryParseLine_ParsesTokenCountEvent()
    {
        var line = "{\"timestamp\":\"2026-01-16T04:44:17.511Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":10,\"output_tokens\":2,\"total_tokens\":12}}}}";

        var ok = LogParser.TryParseLine(line, ProviderKind.Codex, "source.jsonl", out var record);

        Assert.True(ok);
        Assert.Equal(ProviderKind.Codex, record.Provider);
        Assert.Equal(10, record.InputTokens);
        Assert.Equal(2, record.OutputTokens);
        Assert.Equal(12, record.TotalTokens);
    }
}
