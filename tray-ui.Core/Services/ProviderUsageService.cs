using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class ProviderUsageService
{
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyDictionary<ProviderKind, IProviderUsageFetcher> _fetchers;

    public ProviderUsageService(HttpClient? httpClient = null, IEnumerable<IProviderUsageFetcher>? fetchers = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var configured = fetchers?.ToList()
            ?? ProviderFetcherFactory.CreateDefault(_httpClient).ToList();
        _fetchers = configured.ToDictionary(fetcher => fetcher.Kind);
    }

    public async Task<IReadOnlyList<ProviderUsageSnapshot>> FetchAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var snapshots = new List<ProviderUsageSnapshot>();

        foreach (var provider in settings.EnumerateProviders().Select(entry => entry.Key).OrderBy(provider => provider))
        {
            var providerSettings = settings.GetProviderSettings(provider);
            if (!providerSettings.Enabled)
            {
                continue;
            }

            if (!_fetchers.TryGetValue(provider, out var fetcher))
            {
                snapshots.Add(new ProviderUsageSnapshot
                {
                    Provider = provider,
                    SourceLabel = "auto",
                    Error = "No provider fetcher configured.",
                    UpdatedAt = DateTimeOffset.Now
                });
                continue;
            }

            var snapshot = await fetcher.FetchAsync(settings, providerSettings, cancellationToken);
            if (snapshot != null)
            {
                snapshots.Add(snapshot);
            }
            else
            {
                snapshots.Add(new ProviderUsageSnapshot
                {
                    Provider = provider,
                    SourceLabel = "auto",
                    Error = $"No {provider} usage sources available.",
                    UpdatedAt = DateTimeOffset.Now
                });
            }
        }

        return snapshots;
    }
}

internal sealed class CodexOAuthCredentials
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string? IdToken { get; init; }
    public string? AccountId { get; init; }
    public DateTimeOffset? LastRefresh { get; init; }

    public bool NeedsRefresh
    {
        get
        {
            if (LastRefresh == null)
            {
                return true;
            }

            return DateTimeOffset.Now - LastRefresh.Value > TimeSpan.FromDays(8);
        }
    }
}

internal static class CodexOAuthCredentialsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CodexOAuthCredentials? Load()
    {
        var path = ResolveAuthPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("OPENAI_API_KEY", out var apiKeyElement))
            {
                var apiKey = apiKeyElement.GetString();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    return new CodexOAuthCredentials { AccessToken = apiKey.Trim() };
                }
            }

            if (!root.TryGetProperty("tokens", out var tokens))
            {
                return null;
            }

            var access = GetString(tokens, "access_token");
            if (string.IsNullOrWhiteSpace(access))
            {
                return null;
            }

            var refresh = GetString(tokens, "refresh_token") ?? string.Empty;
            var idToken = GetString(tokens, "id_token");
            var accountId = GetString(tokens, "account_id");
            var lastRefresh = ParseIsoDate(GetString(root, "last_refresh"));

            return new CodexOAuthCredentials
            {
                AccessToken = access,
                RefreshToken = refresh,
                IdToken = idToken,
                AccountId = accountId,
                LastRefresh = lastRefresh
            };
        }
        catch
        {
            return null;
        }
    }

    public static void Save(CodexOAuthCredentials credentials)
    {
        var path = ResolveAuthPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        var json = new Dictionary<string, object?>
        {
            ["last_refresh"] = DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture),
            ["tokens"] = new Dictionary<string, object?>
            {
                ["access_token"] = credentials.AccessToken,
                ["refresh_token"] = credentials.RefreshToken,
                ["id_token"] = credentials.IdToken,
                ["account_id"] = credentials.AccountId
            }
        };

        var content = JsonSerializer.Serialize(json, SerializerOptions);
        File.WriteAllText(path, content);
    }

    public static string? ResolveEmail(CodexOAuthCredentials credentials)
    {
        if (string.IsNullOrWhiteSpace(credentials.IdToken))
        {
            return null;
        }

        if (!TryParseJwt(credentials.IdToken, out var payload))
        {
            return null;
        }

        if (payload.TryGetProperty("email", out var emailElement))
        {
            return emailElement.GetString();
        }

        if (payload.TryGetProperty("https://api.openai.com/profile", out var profile) &&
            profile.ValueKind == JsonValueKind.Object &&
            profile.TryGetProperty("email", out var profileEmail))
        {
            return profileEmail.GetString();
        }

        return null;
    }

    public static string? ResolvePlan(CodexUsageResponse usage, CodexOAuthCredentials credentials)
    {
        if (!string.IsNullOrWhiteSpace(usage.PlanType))
        {
            return usage.PlanType;
        }

        if (string.IsNullOrWhiteSpace(credentials.IdToken))
        {
            return null;
        }

        if (!TryParseJwt(credentials.IdToken, out var payload))
        {
            return null;
        }

        if (payload.TryGetProperty("chatgpt_plan_type", out var plan))
        {
            return plan.GetString();
        }

        if (payload.TryGetProperty("https://api.openai.com/auth", out var auth) &&
            auth.ValueKind == JsonValueKind.Object &&
            auth.TryGetProperty("chatgpt_plan_type", out var authPlan))
        {
            return authPlan.GetString();
        }

        return null;
    }

    private static string ResolveAuthPath()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome, "auth.json");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex", "auth.json");
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DateTimeOffset? ParseIsoDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryParseJwt(string token, out JsonElement payload)
    {
        payload = default;
        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        var json = Base64UrlDecode(parts[1]);
        if (json == null)
        {
            return false;
        }

        using var doc = JsonDocument.Parse(json);
        payload = doc.RootElement.Clone();
        return true;
    }

    private static string? Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2:
                padded += "==";
                break;
            case 3:
                padded += "=";
                break;
        }

        try
        {
            var data = Convert.FromBase64String(padded);
            return Encoding.UTF8.GetString(data);
        }
        catch
        {
            return null;
        }
    }
}

internal static class CodexTokenRefresher
{
    private const string RefreshEndpoint = "https://auth.openai.com/oauth/token";
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    public static async Task<CodexOAuthCredentials> RefreshAsync(
        HttpClient httpClient,
        CodexOAuthCredentials credentials,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = credentials.RefreshToken,
            ["scope"] = "openid profile email"
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await httpClient.PostAsync(RefreshEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Codex refresh token expired. Run `codex` to log in again.");
        }

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var accessToken = GetString(root, "access_token") ?? credentials.AccessToken;
        var refreshToken = GetString(root, "refresh_token") ?? credentials.RefreshToken;
        var idToken = GetString(root, "id_token") ?? credentials.IdToken;

        return new CodexOAuthCredentials
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            IdToken = idToken,
            AccountId = credentials.AccountId,
            LastRefresh = DateTimeOffset.Now
        };
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}

internal sealed class CodexUsageResponse
{
    [JsonPropertyName("plan_type")]
    public string? PlanType { get; set; }

    [JsonPropertyName("rate_limit")]
    public CodexRateLimit? RateLimit { get; set; }

    [JsonPropertyName("credits")]
    public CodexCredits? Credits { get; set; }
}

internal sealed class CodexRateLimit
{
    [JsonPropertyName("primary_window")]
    public CodexUsageWindow? PrimaryWindow { get; set; }

    [JsonPropertyName("secondary_window")]
    public CodexUsageWindow? SecondaryWindow { get; set; }
}

internal sealed class CodexUsageWindow
{
    [JsonPropertyName("used_percent")]
    public int? UsedPercent { get; set; }

    [JsonPropertyName("reset_at")]
    public long? ResetAtUnixSeconds { get; set; }

    [JsonPropertyName("limit_window_seconds")]
    public int? WindowSeconds { get; set; }

    [JsonIgnore]
    public int? WindowMinutes => WindowSeconds.HasValue ? WindowSeconds.Value / 60 : null;
}

internal sealed class CodexCredits
{
    [JsonPropertyName("has_credits")]
    public bool HasCredits { get; set; }

    [JsonPropertyName("unlimited")]
    public bool Unlimited { get; set; }

    [JsonPropertyName("balance")]
    public JsonElement Balance { get; set; }

    public string? ToDisplayText()
    {
        if (!HasCredits)
        {
            return null;
        }

        if (Unlimited)
        {
            return "Credits: Unlimited";
        }

        if (Balance.ValueKind == JsonValueKind.Number && Balance.TryGetDouble(out var value))
        {
            return $"Credits: {value:0.##}";
        }

        if (Balance.ValueKind == JsonValueKind.String && double.TryParse(Balance.GetString(), out var parsed))
        {
            return $"Credits: {parsed:0.##}";
        }

        return "Credits: Available";
    }
}

internal static class CodexOAuthUsageFetcher
{
    private const string DefaultChatGptBaseUrl = "https://chatgpt.com/backend-api";

    public static async Task<CodexUsageResponse> FetchUsageAsync(
        HttpClient httpClient,
        string accessToken,
        string? accountId,
        CancellationToken cancellationToken)
    {
        var url = ResolveUsageUrl();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("User-Agent", "WinCodexBar");
        request.Headers.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(accountId))
        {
            request.Headers.Add("ChatGPT-Account-Id", accountId);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Codex OAuth API failed ({(int)response.StatusCode}).");
        }

        return JsonSerializer.Deserialize<CodexUsageResponse>(json) ?? new CodexUsageResponse();
    }

    public static async Task<CodexUsageResponse> FetchUsageWithCookiesAsync(
        HttpClient httpClient,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        var url = ResolveUsageUrl();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("User-Agent", "WinCodexBar");
        request.Headers.Add("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Codex web usage failed ({(int)response.StatusCode}).");
        }

        return JsonSerializer.Deserialize<CodexUsageResponse>(json) ?? new CodexUsageResponse();
    }

    private static string ResolveUsageUrl()
    {
        var baseUrl = ResolveChatGptBaseUrl();
        if (baseUrl.Contains("/backend-api", StringComparison.OrdinalIgnoreCase))
        {
            return baseUrl.TrimEnd('/') + "/wham/usage";
        }

        return baseUrl.TrimEnd('/') + "/api/codex/usage";
    }

    private static string ResolveChatGptBaseUrl()
    {
        var configPath = ResolveCodexConfigPath();
        if (File.Exists(configPath))
        {
            var contents = File.ReadAllText(configPath);
            var parsed = ParseChatGptBaseUrl(contents);
            if (!string.IsNullOrWhiteSpace(parsed))
            {
                return NormalizeBaseUrl(parsed);
            }
        }

        return DefaultChatGptBaseUrl;
    }

    private static string ResolveCodexConfigPath()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome, "config.toml");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex", "config.toml");
    }

    private static string NormalizeBaseUrl(string value)
    {
        var trimmed = value.Trim().TrimEnd('/');
        if ((trimmed.StartsWith("https://chatgpt.com", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://chat.openai.com", StringComparison.OrdinalIgnoreCase)) &&
            !trimmed.Contains("/backend-api", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/backend-api";
        }
        return trimmed;
    }

    private static string? ParseChatGptBaseUrl(string contents)
    {
        foreach (var rawLine in contents.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            if (!string.Equals(parts[0], "chatgpt_base_url", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = parts[1].Trim().Trim('"', '\'');
            return value;
        }

        return null;
    }
}

internal sealed class CodexCliResult
{
    public string SourceLabel { get; init; } = "codex-cli";
    public UsageWindow? Primary { get; init; }
    public UsageWindow? Secondary { get; init; }
    public string? CreditsText { get; init; }
    public string? AccountEmail { get; init; }
    public string? AccountPlan { get; init; }
}

internal static class CodexCliClient
{
    public static async Task<CodexCliResult?> FetchAsync(CancellationToken cancellationToken)
    {
        var rpcResult = await TryFetchViaRpcAsync(cancellationToken);
        if (rpcResult != null)
        {
            return rpcResult;
        }

        return await TryFetchViaPtyAsync(cancellationToken);
    }

    private static async Task<CodexCliResult?> TryFetchViaRpcAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = new JsonRpcProcessClient("codex", "-s read-only -a untrusted app-server");
            await client.StartAsync(cancellationToken);

            await client.SendAsync(1, "initialize", new { client = new { name = "WinCodexBar", version = "0.1" } }, cancellationToken)
                ;

            var account = await client.SendAsync(2, "account/read", null, cancellationToken);
            var rateLimits = await client.SendAsync(3, "account/rateLimits/read", null, cancellationToken);

            var primary = ParseRateWindow(rateLimits, "primary_window", "primaryWindow", "primary");
            var secondary = ParseRateWindow(rateLimits, "secondary_window", "secondaryWindow", "secondary");
            var creditsText = ParseCreditsText(rateLimits);
            var email = ParseString(account, "email", "user_email");
            var plan = ParseString(account, "plan", "plan_type", "account_plan");

            return new CodexCliResult
            {
                SourceLabel = "codex-cli",
                Primary = primary,
                Secondary = secondary,
                CreditsText = creditsText,
                AccountEmail = email,
                AccountPlan = plan
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<CodexCliResult?> TryFetchViaPtyAsync(CancellationToken cancellationToken)
    {
        var output = await ProcessRunner.RunInteractiveAsync(
            "codex",
            "",
            "/status\n",
            TimeSpan.FromSeconds(8),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var sessionPercent = ExtractPercent(output, "5h", "5 h", "5-hour", "5 hour");
        var weeklyPercent = ExtractPercent(output, "weekly", "week");

        return new CodexCliResult
        {
            SourceLabel = "codex-pty",
            Primary = sessionPercent.HasValue ? new UsageWindow { Label = "Session", UsedPercent = sessionPercent } : null,
            Secondary = weeklyPercent.HasValue ? new UsageWindow { Label = "Weekly", UsedPercent = weeklyPercent } : null
        };
    }

    private static UsageWindow? ParseRateWindow(JsonElement element, params string[] names)
    {
        if (!TryGetChild(element, out var window, names))
        {
            return null;
        }

        var used = GetDouble(window, "used_percent", "utilization");
        var resetAt = GetLong(window, "reset_at", "resetAt");
        var windowSeconds = GetInt(window, "limit_window_seconds", "window_seconds", "windowSeconds");

        var resetsAt = resetAt.HasValue ? DateTimeOffset.FromUnixTimeSeconds(resetAt.Value) : (DateTimeOffset?)null;

        return new UsageWindow
        {
            Label = "Window",
            UsedPercent = used,
            WindowMinutes = windowSeconds.HasValue ? windowSeconds.Value / 60 : null,
            ResetsAt = resetsAt,
            ResetDescription = UsageWindowFormatter.FormatResetDescription(resetsAt)
        };
    }

    private static string? ParseCreditsText(JsonElement element)
    {
        if (TryGetChild(element, out var credits, "credits"))
        {
            var unlimited = GetBool(credits, "unlimited");
            if (unlimited == true)
            {
                return "Credits: Unlimited";
            }

            var balance = GetDouble(credits, "balance", "remaining");
            if (balance.HasValue)
            {
                return $"Credits: {balance.Value:0.##}";
            }
        }

        return null;
    }

    private static string? ParseString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static double? ExtractPercent(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            var pattern = $"(?i){Regex.Escape(label)}[^\\d%]*(\\d{{1,3}})%";
            var match = Regex.Match(text, pattern);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var value))
            {
                return value;
            }
        }

        var generic = Regex.Match(text, @"(?i)(\d{1,3})%\s*(used|remaining)");
        if (generic.Success && double.TryParse(generic.Groups[1].Value, out var genericValue))
        {
            return genericValue;
        }

        return null;
    }

    private static bool TryGetChild(JsonElement element, out JsonElement child, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out child))
            {
                return true;
            }
        }

        child = default;
        return false;
    }

    private static double? GetDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetDouble(out var result))
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static int? GetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt32(out var result))
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static long? GetLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out var result))
                {
                    return result;
                }
            }
        }

        return null;
    }

    private static bool? GetBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            if (element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return null;
    }
}

internal sealed class JsonRpcProcessClient : IDisposable
{
    private readonly string _fileName;
    private readonly string _arguments;
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;

    public JsonRpcProcessClient(string fileName, string arguments)
    {
        _fileName = fileName;
        _arguments = arguments;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _fileName,
                Arguments = _arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        _process.Start();
        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;

        await Task.Delay(200, cancellationToken);
    }

    public async Task<JsonElement> SendAsync(int id, string method, object? parameters, CancellationToken cancellationToken)
    {
        if (_stdin == null || _stdout == null)
        {
            throw new InvalidOperationException("RPC process not started.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };

        var json = JsonSerializer.Serialize(payload);
        await _stdin.WriteLineAsync(json.AsMemory(), cancellationToken);
        await _stdin.FlushAsync();

        var timeoutAt = DateTimeOffset.Now.AddSeconds(5);
        while (DateTimeOffset.Now < timeoutAt)
        {
            var line = await _stdout.ReadLineAsync();
            if (line == null)
            {
                break;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var responseId) && responseId.ValueKind == JsonValueKind.Number &&
                responseId.GetInt32() == id &&
                root.TryGetProperty("result", out var result))
            {
                return result.Clone();
            }
        }

        throw new InvalidOperationException("Codex RPC response timed out.");
    }

    public void Dispose()
    {
        try
        {
            _stdin?.Dispose();
            _stdout?.Dispose();
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
            }
        }
        catch
        {
        }
    }
}

internal sealed class ClaudeOAuthCredentials
{
    public string AccessToken { get; init; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; init; }
    public List<string> Scopes { get; init; } = new();
    public string? RateLimitTier { get; init; }

    public bool IsExpired => ExpiresAt != null && ExpiresAt <= DateTimeOffset.Now;
}

internal static class ClaudeOAuthCredentialsStore
{
    public static ClaudeOAuthCredentials? Load()
    {
        var path = ResolveCredentialsPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!TryGetChild(root, out var oauth, "claudeAiOauth", "claude_ai_oauth"))
            {
                return null;
            }

            var accessToken = GetString(oauth, "accessToken") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return null;
            }

            var expiresAt = GetDouble(oauth, "expiresAt");
            var scopes = GetStringArray(oauth, "scopes");
            var rateLimitTier = GetString(oauth, "rateLimitTier");

            return new ClaudeOAuthCredentials
            {
                AccessToken = accessToken,
                ExpiresAt = expiresAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)expiresAt.Value) : null,
                Scopes = scopes,
                RateLimitTier = rateLimitTier
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveCredentialsPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", ".credentials.json");
    }

    private static bool TryGetChild(JsonElement element, out JsonElement child, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out child))
            {
                return true;
            }
        }

        child = default;
        return false;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static List<string> GetStringArray(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        list.Add(text);
                    }
                }
            }
            return list;
        }

        return new List<string>();
    }
}

internal sealed class ClaudeOAuthUsageResponse
{
    [JsonPropertyName("five_hour")]
    public ClaudeOAuthWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public ClaudeOAuthWindow? SevenDay { get; set; }
}

internal sealed class ClaudeOAuthWindow
{
    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAtRaw { get; set; }

    [JsonIgnore]
    public DateTimeOffset? ResetsAt => ParseIso(ResetsAtRaw);

    private static DateTimeOffset? ParseIso(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}

internal static class ClaudeOAuthUsageFetcher
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeader = "oauth-2025-04-20";

    public static async Task<ClaudeOAuthUsageResponse> FetchUsageAsync(
        HttpClient httpClient,
        string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Content-Type", "application/json");
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("User-Agent", "WinCodexBar");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Claude OAuth API failed ({(int)response.StatusCode}).");
        }

        return JsonSerializer.Deserialize<ClaudeOAuthUsageResponse>(json) ?? new ClaudeOAuthUsageResponse();
    }
}

internal sealed class ClaudeWebUsageResult
{
    public double SessionPercentUsed { get; init; }
    public DateTimeOffset? SessionResetsAt { get; init; }
    public double? WeeklyPercentUsed { get; init; }
    public DateTimeOffset? WeeklyResetsAt { get; init; }
    public string? AccountEmail { get; init; }
    public string? LoginMethod { get; init; }
    public string? ExtraUsageText { get; init; }
}

internal static class ClaudeWebApiFetcher
{
    public static async Task<ClaudeWebUsageResult> FetchUsageAsync(
        HttpClient httpClient,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        var sessionKey = ExtractSessionKey(cookieHeader);
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            throw new InvalidOperationException("Claude sessionKey cookie missing.");
        }

        var orgId = await FetchOrganizationIdAsync(httpClient, sessionKey, cancellationToken);
        var usage = await FetchUsageAsync(httpClient, sessionKey, orgId, cancellationToken);
        var account = await FetchAccountAsync(httpClient, sessionKey, cancellationToken);
        var extraUsage = await FetchExtraUsageAsync(httpClient, sessionKey, orgId, cancellationToken);

        return new ClaudeWebUsageResult
        {
            SessionPercentUsed = usage.SessionPercentUsed,
            SessionResetsAt = usage.SessionResetsAt,
            WeeklyPercentUsed = usage.WeeklyPercentUsed,
            WeeklyResetsAt = usage.WeeklyResetsAt,
            AccountEmail = account?.Email,
            LoginMethod = account?.LoginMethod,
            ExtraUsageText = extraUsage
        };
    }

    private static async Task<string> FetchOrganizationIdAsync(HttpClient httpClient, string sessionKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://claude.ai/api/organizations");
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        request.Headers.Add("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Claude org lookup failed ({(int)response.StatusCode}).");
        }

        var orgs = JsonSerializer.Deserialize<List<ClaudeOrgResponse>>(json) ?? new List<ClaudeOrgResponse>();
        var selected = orgs.Find(org => org.Capabilities?.Contains("chat", StringComparer.OrdinalIgnoreCase) == true)
            ?? orgs.Find(org => org.Capabilities?.Contains("api", StringComparer.OrdinalIgnoreCase) != true)
            ?? (orgs.Count > 0 ? orgs[0] : null);

        if (selected == null || string.IsNullOrWhiteSpace(selected.Uuid))
        {
            throw new InvalidOperationException("No Claude organization found.");
        }

        return selected.Uuid;
    }

    private static async Task<ClaudeWebUsagePayload> FetchUsageAsync(
        HttpClient httpClient,
        string sessionKey,
        string orgId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://claude.ai/api/organizations/{orgId}/usage");
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        request.Headers.Add("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Claude usage failed ({(int)response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var fiveHour = root.TryGetProperty("five_hour", out var session) ? session : default;
        var sevenDay = root.TryGetProperty("seven_day", out var weekly) ? weekly : default;

        var sessionUtil = GetDouble(fiveHour, "utilization") ?? 0;
        var sessionResetsAt = ParseIsoDate(GetString(fiveHour, "resets_at"));
        var weeklyUtil = GetDouble(sevenDay, "utilization");
        var weeklyResetsAt = ParseIsoDate(GetString(sevenDay, "resets_at"));

        return new ClaudeWebUsagePayload
        {
            SessionPercentUsed = sessionUtil,
            SessionResetsAt = sessionResetsAt,
            WeeklyPercentUsed = weeklyUtil,
            WeeklyResetsAt = weeklyResetsAt
        };
    }

    private static async Task<ClaudeWebAccount?> FetchAccountAsync(HttpClient httpClient, string sessionKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://claude.ai/api/account");
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        request.Headers.Add("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return JsonSerializer.Deserialize<ClaudeWebAccount>(json);
    }

    private static async Task<string?> FetchExtraUsageAsync(
        HttpClient httpClient,
        string sessionKey,
        string orgId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://claude.ai/api/organizations/{orgId}/overage_spend_limit");
        request.Headers.Add("Cookie", $"sessionKey={sessionKey}");
        request.Headers.Add("Accept", "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var enabled = GetBool(root, "is_enabled");
            if (enabled != true)
            {
                return null;
            }

            var used = GetDouble(root, "used_credits");
            var limit = GetDouble(root, "monthly_credit_limit");
            var currency = GetString(root, "currency");

            if (used.HasValue && limit.HasValue && !string.IsNullOrWhiteSpace(currency))
            {
                return $"Extra usage: {used.Value / 100:0.##}/{limit.Value / 100:0.##} {currency}";
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? ExtractSessionKey(string cookieHeader)
    {
        foreach (var pair in CookieHeaderParser.Parse(cookieHeader))
        {
            if (string.Equals(pair.Name, "sessionKey", StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? GetBool(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value))
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseIsoDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class ClaudeOrgResponse
    {
        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("capabilities")]
        public List<string>? Capabilities { get; set; }
    }

    private sealed class ClaudeWebUsagePayload
    {
        public double SessionPercentUsed { get; init; }
        public DateTimeOffset? SessionResetsAt { get; init; }
        public double? WeeklyPercentUsed { get; init; }
        public DateTimeOffset? WeeklyResetsAt { get; init; }
    }

    private sealed class ClaudeWebAccount
    {
        [JsonPropertyName("email_address")]
        public string? Email { get; set; }

        public string? LoginMethod { get; set; }
    }
}

internal sealed class ClaudeCliResult
{
    public string SourceLabel { get; init; } = "claude-cli";
    public UsageWindow? Primary { get; init; }
    public UsageWindow? Secondary { get; init; }
    public string? AccountEmail { get; init; }
    public string? AccountPlan { get; init; }
}

internal static class ClaudeCliClient
{
    public static async Task<ClaudeCliResult?> FetchAsync(CancellationToken cancellationToken)
    {
        var output = await ProcessRunner.RunInteractiveAsync(
            "claude",
            "--allowed-tools \"\"",
            "/usage\n",
            TimeSpan.FromSeconds(10),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var sessionPercent = ExtractPercent(output, "current session", "session");
        var weeklyPercent = ExtractPercent(output, "current week", "weekly", "week");

        return new ClaudeCliResult
        {
            SourceLabel = "claude-cli",
            Primary = sessionPercent.HasValue ? new UsageWindow { Label = "Session", UsedPercent = sessionPercent } : null,
            Secondary = weeklyPercent.HasValue ? new UsageWindow { Label = "Weekly", UsedPercent = weeklyPercent } : null
        };
    }

    private static double? ExtractPercent(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            var pattern = $"(?i){Regex.Escape(label)}[^\\d%]*(\\d{{1,3}})%";
            var match = Regex.Match(text, pattern, RegexOptions.Singleline);
            if (match.Success && double.TryParse(match.Groups[1].Value, out var value))
            {
                return value;
            }
        }

        var generic = Regex.Match(text, @"(?i)(\d{1,3})%\s*(used|remaining)", RegexOptions.Singleline);
        if (generic.Success && double.TryParse(generic.Groups[1].Value, out var genericValue))
        {
            return genericValue;
        }

        return null;
    }
}

internal static class ProcessRunner
{
    public static async Task<string?> RunInteractiveAsync(
        string fileName,
        string arguments,
        string input,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        if (!process.Start())
        {
            return null;
        }

        await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync();

        var output = new StringBuilder();
        var started = DateTimeOffset.Now;

        while (!process.HasExited && DateTimeOffset.Now - started < timeout)
        {
            var line = await process.StandardOutput.ReadLineAsync();
            if (line != null)
            {
                output.AppendLine(line);
                continue;
            }

            await Task.Delay(50, cancellationToken);
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch
        {
        }

        var stderr = await process.StandardError.ReadToEndAsync();
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            output.AppendLine(stderr);
        }

        var text = output.ToString().Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}

internal static class CookieHeaderParser
{
    public static IEnumerable<(string Name, string Value)> Parse(string header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            yield break;
        }

        var parts = header.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var idx = trimmed.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var name = trimmed.Substring(0, idx).Trim();
            var value = trimmed.Substring(idx + 1).Trim();
            if (name.Length == 0 || value.Length == 0)
            {
                continue;
            }

            yield return (name, value);
        }
    }
}
