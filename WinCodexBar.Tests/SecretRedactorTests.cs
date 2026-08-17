using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class SecretRedactorTests
{
    [Fact]
    public void Redact_MasksBearerTokens()
    {
        var redacted = SecretRedactor.Redact("authorization: Bearer sk-abc123DEF456ghi");

        Assert.Equal("authorization: Bearer ***", redacted);
    }

    [Fact]
    public void Redact_MasksJsonTokenValues()
    {
        var redacted = SecretRedactor.Redact("""{"access_token":"secret-value","used_percent":42}""");

        Assert.DoesNotContain("secret-value", redacted);
        Assert.Contains("used_percent", redacted);
    }

    [Fact]
    public void Redact_MasksCookieValues()
    {
        var redacted = SecretRedactor.Redact("Cookie: sessionKey=sk-ant-xyz; theme=dark");

        Assert.DoesNotContain("sk-ant-xyz", redacted);
        Assert.Contains("sessionKey=***", redacted);
        Assert.Contains("theme=dark", redacted);
    }

    [Fact]
    public void Redact_MasksJsonWebTokens()
    {
        var jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJ1c2VyXzAxIn0.abcDEF123";

        var redacted = SecretRedactor.Redact($"token is {jwt} here");

        Assert.DoesNotContain(jwt, redacted);
        Assert.Equal("token is *** here", redacted);
    }

    [Fact]
    public void Redact_LeavesOrdinaryDiagnosticsTextAlone()
    {
        const string text = "status: 401 Unauthorized\nurl: https://api.anthropic.com/api/oauth/usage";

        Assert.Equal(text, SecretRedactor.Redact(text));
    }
}
