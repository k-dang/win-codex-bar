using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class DiagnosticsDetailTests
{
    [Fact]
    public void Truncate_ShortensLongBodiesAndReportsTheRemainder()
    {
        var payload = DiagnosticsDetail.Truncate(new string('x', 50), maxLength: 10);

        Assert.NotNull(payload);
        Assert.StartsWith(new string('x', 10), payload);
        Assert.Contains("+40 chars", payload);
    }

    [Fact]
    public void Truncate_ReturnsNullForEmptyText()
    {
        Assert.Null(DiagnosticsDetail.Truncate("   ", maxLength: 100));
    }

    [Fact]
    public void Indent_PrefixesEveryDetailLine()
    {
        var indented = DiagnosticsDetail.Indent("url: https://example.test\r\nstatus: 401");

        Assert.Equal(
            $"    url: https://example.test{Environment.NewLine}    status: 401",
            indented);
    }

    // Composition leaves secrets alone on purpose; the sinks that store or display an
    // entry are what redact.
    [Fact]
    public void Compose_KeepsFieldsVerbatim()
    {
        var detail = DiagnosticsDetail.Compose(
            ("status", "401 Unauthorized"),
            ("skipped", null),
            ("body", "  {\"used\":1}  "));

        Assert.Equal($"status: 401 Unauthorized{Environment.NewLine}body: {{\"used\":1}}", detail);
    }
}
