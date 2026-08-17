using System.Text.RegularExpressions;

namespace WinCodexBar.Core.Services;

// Diagnostics entries are written to disk and meant to be shareable, so anything
// token-shaped is masked before it is stored.
public static class SecretRedactor
{
    private const string Mask = "***";
    private static readonly RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly Regex BearerPattern = new(
        @"\bBearer\s+[A-Za-z0-9\-\._~\+/=]+",
        Options);

    // "access_token": "...", "sessionKey": "...", and friends.
    private static readonly Regex JsonSecretPattern = new(
        @"""(?<name>[A-Za-z0-9_\-]*(?:access_?token|refresh_?token|id_?token|api[_\-]?key|session_?key|session_?token|secret|password|cookie)[A-Za-z0-9_\-]*)""\s*:\s*""[^""]*""",
        Options);

    // sessionKey=..., WorkosCursorSessionToken=..., __Secure-next-auth.session-token=...
    private static readonly Regex CookiePattern = new(
        @"\b(?<name>session_?key|session_?token|[A-Za-z0-9_\-]*auth[A-Za-z0-9_\-\.]*token|__Secure-[A-Za-z0-9_\-\.]+|__Host-[A-Za-z0-9_\-\.]+|cf_clearance|oai-[A-Za-z0-9_\-\.]+)=[^;,\s""]+",
        Options);

    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+(?:\.[A-Za-z0-9_\-]+)?",
        RegexOptions.CultureInvariant);

    private static readonly Regex ApiKeyPattern = new(
        @"\bsk-[A-Za-z0-9\-_]{8,}",
        RegexOptions.CultureInvariant);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = BearerPattern.Replace(text, $"Bearer {Mask}");
        result = JsonSecretPattern.Replace(result, match => $"\"{match.Groups["name"].Value}\": \"{Mask}\"");
        result = CookiePattern.Replace(result, match => $"{match.Groups["name"].Value}={Mask}");
        result = JwtPattern.Replace(result, Mask);
        result = ApiKeyPattern.Replace(result, Mask);
        return result;
    }

}
