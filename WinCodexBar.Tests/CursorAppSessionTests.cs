using System.Buffers.Text;
using System.Text.Json;
using WinCodexBar.Core.Models;
using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class CursorAppSessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_750_000_000);

    [Fact]
    public void TryCreate_ValidToken_BuildsExactCookieHeader()
    {
        var token = MakeToken("google-oauth2|user_01ABC", Now.AddHours(1));

        var session = CursorAppSession.TryCreate(token, Now);

        Assert.NotNull(session);
        Assert.Equal("user_01ABC", session!.UserId);
        Assert.Equal($"WorkosCursorSessionToken=user_01ABC%3A%3A{token}", session.CookieHeader);
    }

    [Theory]
    [InlineData("google-oauth2|user_01ABC", "user_01ABC")]
    [InlineData("user_01ABC", "user_01ABC")]
    [InlineData("auth0|tenant|c.d-e_f", "c.d-e_f")]
    [InlineData("google-oauth2|user_01ABC|", "user_01ABC")]
    public void TryCreate_VariedSubShapes_ExtractsSegmentAfterLastSeparator(string sub, string expectedUserId)
    {
        var session = CursorAppSession.TryCreate(MakeToken(sub, Now.AddHours(1)), Now);

        Assert.Equal(expectedUserId, session?.UserId);
    }

    [Theory]
    [InlineData("google-oauth2|user 01")]
    [InlineData("google-oauth2|user@bad")]
    [InlineData("google-oauth2|user:01")]
    [InlineData("|")]
    [InlineData("")]
    public void TryCreate_MalformedUserId_ReturnsNull(string sub)
    {
        Assert.Null(CursorAppSession.TryCreate(MakeToken(sub, Now.AddHours(1)), Now));
    }

    [Fact]
    public void TryCreate_MissingSub_ReturnsNull()
    {
        var token = MakeTokenFromPayload(new Dictionary<string, object>
        {
            ["exp"] = Now.AddHours(1).ToUnixTimeSeconds()
        });

        Assert.Null(CursorAppSession.TryCreate(token, Now));
    }

    [Fact]
    public void TryCreate_ExpMoreThan60SecondsAhead_IsUsable()
    {
        Assert.NotNull(CursorAppSession.TryCreate(MakeToken("auth0|user_01", Now.AddSeconds(61)), Now));
    }

    [Theory]
    [InlineData(60)]
    [InlineData(30)]
    [InlineData(-3600)]
    public void TryCreate_ExpWithin60SecondsOrPast_ReturnsNull(int secondsFromNow)
    {
        Assert.Null(CursorAppSession.TryCreate(MakeToken("auth0|user_01", Now.AddSeconds(secondsFromNow)), Now));
    }

    [Fact]
    public void TryCreate_MissingExp_ReturnsNull()
    {
        var token = MakeTokenFromPayload(new Dictionary<string, object> { ["sub"] = "auth0|user_01" });

        Assert.Null(CursorAppSession.TryCreate(token, Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_EmptyToken_ReturnsNull(string? token)
    {
        Assert.Null(CursorAppSession.TryCreate(token, Now));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("header.!!!invalid-base64!!!.signature")]
    [InlineData("header..signature")]
    public void TryCreate_InvalidJwt_ReturnsNull(string token)
    {
        Assert.Null(CursorAppSession.TryCreate(token, Now));
    }

    [Fact]
    public void TryCreate_PayloadNotAJsonObject_ReturnsNull()
    {
        var payload = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes("just a string"));

        Assert.Null(CursorAppSession.TryCreate($"header.{payload}.signature", Now));
    }

    internal static string MakeToken(string sub, DateTimeOffset expiresAt)
    {
        return MakeTokenFromPayload(new Dictionary<string, object>
        {
            ["sub"] = sub,
            ["exp"] = expiresAt.ToUnixTimeSeconds()
        });
    }

    private static string MakeTokenFromPayload(Dictionary<string, object> payload)
    {
        static string Encode(object value) =>
            Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));

        return $"{Encode(new Dictionary<string, object> { ["alg"] = "RS256", ["typ"] = "JWT" })}.{Encode(payload)}.signature";
    }
}

public class CursorAppTokenUsageSourceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("not-a-jwt")]
    public async Task FetchAsync_MissingOrInvalidLocalToken_ReturnsNullWithoutHttp(string? token)
    {
        var source = new CursorAppTokenUsageSource(
            new HttpClient(new ThrowingHandler()),
            new FakeTokenReader(token));

        var snapshot = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task FetchAsync_ExpiredLocalToken_ReturnsNullWithoutHttp()
    {
        var expired = CursorAppSessionTests.MakeToken("auth0|user_01", DateTimeOffset.UtcNow.AddMinutes(-5));
        var source = new CursorAppTokenUsageSource(
            new HttpClient(new ThrowingHandler()),
            new FakeTokenReader(expired));

        var snapshot = await source.FetchAsync(CreateRequest(), CancellationToken.None);

        Assert.Null(snapshot);
    }

    private static ProviderUsageSourceRequest CreateRequest()
    {
        var settings = AppSettings.CreateDefault();
        return new ProviderUsageSourceRequest(
            ProviderKind.Cursor,
            settings.GetProviderSettings(ProviderKind.Cursor),
            settings);
    }

    private sealed class FakeTokenReader : ICursorLocalTokenReader
    {
        private readonly string? _token;

        public FakeTokenReader(string? token)
        {
            _token = token;
        }

        public string? ReadAccessToken() => _token;
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("An unavailable local token must not trigger HTTP requests.");
        }
    }
}
