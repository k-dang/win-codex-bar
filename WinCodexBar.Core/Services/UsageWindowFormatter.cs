namespace WinCodexBar.Core.Services;

public static class UsageWindowFormatter
{
    public static string? FormatResetDescription(DateTimeOffset? resetsAt)
    {
        if (resetsAt == null)
        {
            return null;
        }

        var now = DateTimeOffset.Now;
        if (resetsAt <= now)
        {
            return "Reset time passed";
        }

        var remaining = resetsAt.Value - now;
        if (remaining.TotalHours >= 1)
        {
            var hours = (int)Math.Floor(remaining.TotalHours);
            var minutes = remaining.Minutes;
            return minutes > 0 ? $"Resets in {hours}h {minutes}m" : $"Resets in {hours}h";
        }

        if (remaining.TotalMinutes >= 1)
        {
            return $"Resets in {(int)Math.Floor(remaining.TotalMinutes)}m";
        }

        return "Resets soon";
    }
}

