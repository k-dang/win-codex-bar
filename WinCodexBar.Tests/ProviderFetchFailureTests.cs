using System.Net;
using System.Text;
using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class ProviderFetchFailureTests
{
    [Fact]
    public async Task ClaudeOAuthFetch_WhenUnauthorized_CarriesUrlStatusAndBody()
    {
        var handler = new StubHandler(
            HttpStatusCode.Unauthorized,
            """{"error":{"type":"authentication_error","message":"OAuth token has expired"}}""");

        var exception = await Assert.ThrowsAsync<ProviderFetchException>(() =>
            ClaudeOAuthUsageFetcher.FetchUsageAsync(new HttpClient(handler), "token", CancellationToken.None));

        var detail = DiagnosticsDetail.FromException(exception);
        Assert.NotNull(detail);
        Assert.Contains("exception: ProviderFetchException: Claude OAuth API failed (401).", detail);
        Assert.Contains("url: https://api.anthropic.com/api/oauth/usage", detail);
        Assert.Contains("status: 401 Unauthorized", detail);
        Assert.Contains("OAuth token has expired", detail);
    }

    [Fact]
    public async Task CodexOAuthFetch_WhenBodyIsNotJson_KeepsTheOffendingBody()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "<html>Access denied</html>");

        var exception = await Assert.ThrowsAsync<ProviderFetchException>(() =>
            CodexOAuthUsageFetcher.FetchUsageAsync(new HttpClient(handler), "token", "acct_1", CancellationToken.None));

        var detail = DiagnosticsDetail.FromException(exception);
        Assert.NotNull(detail);
        Assert.Contains("Codex OAuth API returned malformed JSON.", detail);
        Assert.Contains("<html>Access denied</html>", detail);
        Assert.Contains("caused by: JsonException", detail);
    }

    [Fact]
    public async Task CodexOAuthFetch_WhenUnauthorized_DoesNotLeakTheAccessToken()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"access_token":"leaked-value"}""");

        var exception = await Assert.ThrowsAsync<ProviderFetchException>(() =>
            CodexOAuthUsageFetcher.FetchUsageAsync(
                new HttpClient(handler),
                "sk-super-secret-token",
                accountId: null,
                CancellationToken.None));

        var detail = SecretRedactor.Redact(DiagnosticsDetail.FromException(exception));
        Assert.DoesNotContain("sk-super-secret-token", detail);
        Assert.DoesNotContain("leaked-value", detail);
        Assert.Contains("account-id: not sent", detail);
    }

    [Fact]
    public async Task RunInteractiveAsync_WhenExecutableIsMissing_ExplainsWithTheCommand()
    {
        var exception = await Assert.ThrowsAsync<ProviderFetchException>(() =>
            ProcessRunner.RunInteractiveAsync(
                "wincodexbar-not-a-real-tool",
                "--version",
                string.Empty,
                TimeSpan.FromSeconds(1),
                CancellationToken.None));

        Assert.Contains("Is it installed and on PATH?", exception.Message);
        Assert.NotNull(exception.Detail);
        Assert.Contains("command: wincodexbar-not-a-real-tool --version", exception.Detail);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                RequestMessage = request,
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
        }
    }
}
