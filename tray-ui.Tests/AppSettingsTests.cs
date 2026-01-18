using tray_ui.Models;
using Xunit;

namespace tray_ui.Tests;

public class AppSettingsTests
{
    [Fact]
    public void NormalizeRoots_TrimsAndDeduplicates()
    {
        var roots = new[] { "  C:\\Logs  ", "C:\\Logs", "", "   ", "D:\\Logs" };

        var normalized = AppSettings.NormalizeRoots(roots);

        Assert.Equal(2, normalized.Count);
        Assert.Contains("C:\\Logs", normalized);
        Assert.Contains("D:\\Logs", normalized);
    }
}
