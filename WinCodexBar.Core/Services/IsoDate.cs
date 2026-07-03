using System.Globalization;

namespace WinCodexBar.Core.Services;

internal static class IsoDate
{
    public static DateTimeOffset? Parse(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
