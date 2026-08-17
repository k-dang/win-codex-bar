using System.Globalization;
using System.Text;
using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

// Appends diagnostics entries to a rolling text file so failures stay readable after
// the app is closed.
public sealed class DiagnosticsLogFile
{
    public const long DefaultMaxBytes = 1024 * 1024;

    private readonly object _lock = new();
    private readonly long _maxBytes;
    private bool _directoryReady;

    public DiagnosticsLogFile(string directory, long maxBytes = DefaultMaxBytes)
    {
        Directory = directory;
        FilePath = Path.Combine(directory, "diagnostics.log");
        PreviousFilePath = Path.Combine(directory, "diagnostics.1.log");
        _maxBytes = maxBytes;
    }

    public string Directory { get; }
    public string FilePath { get; }
    public string PreviousFilePath { get; }

    public static DiagnosticsLogFile CreateDefault()
    {
        return new DiagnosticsLogFile(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinCodexBar",
            "logs"));
    }

    public void Append(DiagnosticsLogEntry entry)
    {
        lock (_lock)
        {
            try
            {
                if (!_directoryReady)
                {
                    System.IO.Directory.CreateDirectory(Directory);
                    _directoryReady = true;
                }

                RollIfOversized();
                File.AppendAllText(FilePath, Format(entry), Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Diagnostics must never take the app down. Re-check the directory next
                // time in case it was the thing that went missing.
                _directoryReady = false;
            }
        }
    }

    // Redacts here rather than trusting the caller: this is the point where diagnostics
    // text becomes a file on disk that the user is expected to be able to share.
    public static string Format(DiagnosticsLogEntry entry)
    {
        var builder = new StringBuilder();
        builder.Append(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture));
        builder.Append(' ').Append(DiagnosticsEventLabel.For(entry.EventType).ToUpperInvariant().PadRight(10));
        builder.Append(entry.Provider?.ToString() ?? "-");

        if (!string.IsNullOrWhiteSpace(entry.SourceMethod))
        {
            builder.Append('/').Append(entry.SourceMethod);
        }

        if (entry.Duration.HasValue)
        {
            builder.Append(CultureInfo.InvariantCulture, $" ({entry.Duration.Value.TotalMilliseconds:0}ms)");
        }

        builder.Append(' ').AppendLine(SingleLine(SecretRedactor.Redact(entry.Message)));

        if (!string.IsNullOrWhiteSpace(entry.Detail))
        {
            builder.AppendLine(DiagnosticsDetail.Indent(SecretRedactor.Redact(entry.Detail)));
        }

        return builder.ToString();
    }

    private void RollIfOversized()
    {
        var current = new FileInfo(FilePath);
        if (!current.Exists || current.Length < _maxBytes)
        {
            return;
        }

        File.Delete(PreviousFilePath);
        File.Move(FilePath, PreviousFilePath);
    }

    private static string SingleLine(string text)
    {
        return text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
    }
}
