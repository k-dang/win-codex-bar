using System.Net;
using System.Text;
using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class CursorWebApiFetcherTests
{
    private const string SummaryJson = """
    {
        "billingCycleStart": "2026-06-15T00:00:00Z",
        "billingCycleEnd": "2026-07-15T00:00:00Z",
        "individualUsage": { "plan": { "totalPercentUsed": 42.0 } }
    }
    """;

    private const string QuotaJson = """
    { "gpt-4": { "maxRequestUsage": 500, "numRequestsTotal": 125 } }
    """;

    [Fact]
    public async Task FetchUsageAsync_KnownUserId_QueriesQuotaDirectlyWithoutAuthMe()
    {
        var handler = new RoutedHandler
        {
            ["/api/usage-summary"] = _ => Json(SummaryJson),
            ["/api/usage"] = _ => Json(QuotaJson)
        };

        var result = await CursorWebApiFetcher.FetchUsageAsync(
            new HttpClient(handler),
            "cookie",
            knownUserId: "user_known",
            new SourceDiagnostics(),
            CancellationToken.None);

        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath == "/api/auth/me");
        var quotaRequest = Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/api/usage");
        Assert.Equal("?user=user_known", quotaRequest.Query);
        Assert.Equal(new CursorRequestQuota(Used: 125, Limit: 500), result.RequestQuota);
    }

    [Fact]
    public async Task FetchUsageAsync_NoKnownUserId_ResolvesAndNormalizesSubViaAuthMe()
    {
        var handler = new RoutedHandler
        {
            ["/api/usage-summary"] = _ => Json(SummaryJson),
            ["/api/auth/me"] = _ => Json("""{ "sub": "google-oauth2|user_01ABC" }"""),
            ["/api/usage"] = _ => Json(QuotaJson)
        };

        var result = await CursorWebApiFetcher.FetchUsageAsync(
            new HttpClient(handler),
            "cookie",
            knownUserId: null,
            new SourceDiagnostics(),
            CancellationToken.None);

        var quotaRequest = Assert.Single(handler.Requests, uri => uri.AbsolutePath == "/api/usage");
        Assert.Equal("?user=user_01ABC", quotaRequest.Query);
        Assert.Equal(new CursorRequestQuota(Used: 125, Limit: 500), result.RequestQuota);
    }

    [Fact]
    public async Task FetchUsageAsync_NoKnownUserIdAndAuthMeFails_SkipsQuotaButReturnsSummary()
    {
        var handler = new RoutedHandler
        {
            ["/api/usage-summary"] = _ => Json(SummaryJson),
            ["/api/auth/me"] = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        };

        var result = await CursorWebApiFetcher.FetchUsageAsync(
            new HttpClient(handler),
            "cookie",
            knownUserId: null,
            new SourceDiagnostics(),
            CancellationToken.None);

        Assert.DoesNotContain(handler.Requests, uri => uri.AbsolutePath == "/api/usage");
        Assert.Null(result.RequestQuota);
        Assert.Equal(42.0, result.Summary.IndividualUsage?.Plan?.TotalPercentUsed);
    }

    private static HttpResponseMessage Json(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RoutedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
        private readonly List<Uri> _requests = new();

        public Func<HttpRequestMessage, HttpResponseMessage> this[string path]
        {
            set => _routes[path] = value;
        }

        public IReadOnlyList<Uri> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (_requests)
            {
                _requests.Add(uri);
            }

            return Task.FromResult(
                _routes.TryGetValue(uri.AbsolutePath, out var respond)
                    ? respond(request)
                    : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
