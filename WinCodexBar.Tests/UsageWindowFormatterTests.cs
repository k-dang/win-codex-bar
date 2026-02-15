using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class UsageWindowFormatterTests
{
    [Fact]
    public void FormatResetDescription_ReturnsNullWhenResetIsMissing()
    {
        var description = UsageWindowFormatter.FormatResetDescription(null);

        Assert.Null(description);
    }

    [Fact]
    public void FormatResetDescription_ReturnsPassedWhenResetIsPast()
    {
        var resetsAt = DateTimeOffset.Now.AddMinutes(-1);

        var description = UsageWindowFormatter.FormatResetDescription(resetsAt);

        Assert.Equal("Reset time passed", description);
    }

    [Fact]
    public void FormatResetDescription_ReturnsHoursWhenMoreThanHourRemaining()
    {
        var resetsAt = DateTimeOffset.Now.AddHours(2).AddMinutes(30);

        var description = UsageWindowFormatter.FormatResetDescription(resetsAt);

        Assert.NotNull(description);
        Assert.StartsWith("Resets in 2h", description, StringComparison.Ordinal);
        Assert.Contains("m", description, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatResetDescription_ReturnsMinutesWhenLessThanHourRemaining()
    {
        var resetsAt = DateTimeOffset.Now.AddMinutes(10);

        var description = UsageWindowFormatter.FormatResetDescription(resetsAt);

        Assert.NotNull(description);
        Assert.StartsWith("Resets in ", description, StringComparison.Ordinal);
        Assert.EndsWith("m", description, StringComparison.Ordinal);
        Assert.DoesNotContain("h", description, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatResetDescription_ReturnsSoonWhenUnderOneMinute()
    {
        var resetsAt = DateTimeOffset.Now.AddSeconds(30);

        var description = UsageWindowFormatter.FormatResetDescription(resetsAt);

        Assert.Equal("Resets soon", description);
    }
}

