using System.Text;

namespace WinCodexBar.Core.Services;

// Builds the multi-line detail block attached to a diagnostics entry: request URL,
// status, response body, exception chain. Nothing here redacts; secrets are masked once
// at the sinks that store or display an entry (DiagnosticsLogger and DiagnosticsLogFile).
public static class DiagnosticsDetail
{
    public const int MaxBodyLength = 2048;
    private const string IndentPrefix = "    ";
    private const int MaxInnerExceptions = 5;
    private const int MaxStackFrames = 4;

    public static string? Compose(params (string Key, string? Value)[] fields)
    {
        var builder = new StringBuilder();
        foreach (var (key, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            builder.Append(key).Append(": ").AppendLine(value.Trim());
        }

        return NullIfEmpty(builder.ToString());
    }

    public static string? FromException(Exception? exception)
    {
        if (exception == null)
        {
            return null;
        }

        var builder = new StringBuilder();
        var current = exception;
        var depth = 0;

        while (current != null && depth <= MaxInnerExceptions)
        {
            builder
                .Append(depth == 0 ? "exception" : "caused by")
                .Append(": ")
                .Append(current.GetType().Name)
                .Append(": ")
                .AppendLine(current.Message.Trim());

            if (current is ProviderFetchException fetch && !string.IsNullOrWhiteSpace(fetch.Detail))
            {
                builder.AppendLine(fetch.Detail.TrimEnd());
            }

            current = current.InnerException;
            depth++;
        }

        AppendStackFrames(builder, exception);
        return NullIfEmpty(builder.ToString());
    }

    public static string? Join(params string?[] parts)
    {
        var present = parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.TrimEnd());
        return NullIfEmpty(string.Join(Environment.NewLine, present));
    }

    public static string? Body(string? text)
    {
        return Truncate(text, MaxBodyLength);
    }

    public static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : $"{trimmed[..maxLength]}… (+{trimmed.Length - maxLength} chars)";
    }

    // The indent that marks a detail block as belonging to the line above it, shared by
    // the log file and the clipboard copy so the two stay identical.
    public static string Indent(string detail)
    {
        return string.Join(
            Environment.NewLine,
            detail.Split('\n').Select(line => IndentPrefix + line.TrimEnd('\r')));
    }

    private static void AppendStackFrames(StringBuilder builder, Exception exception)
    {
        var stack = exception.StackTrace;
        if (string.IsNullOrWhiteSpace(stack))
        {
            return;
        }

        var frames = stack
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(frame => frame.Trim())
            .Where(frame => frame.Length > 0)
            .Take(MaxStackFrames);

        foreach (var frame in frames)
        {
            builder.Append("stack: ").AppendLine(frame);
        }
    }

    private static string? NullIfEmpty(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
