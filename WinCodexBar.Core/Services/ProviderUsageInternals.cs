using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

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

    public static string AuthPath => ResolveAuthPath();

    public static CodexOAuthCredentials? Load(out string? failureReason)
    {
        var path = ResolveAuthPath();
        if (!File.Exists(path))
        {
            failureReason = $"Codex auth file not found at {path}. Run `codex` to log in.";
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                failureReason = $"Codex auth file is empty ({path}).";
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("OPENAI_API_KEY", out var apiKeyElement))
            {
                var apiKey = apiKeyElement.GetString();
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    failureReason = null;
                    return new CodexOAuthCredentials { AccessToken = apiKey.Trim() };
                }
            }

            if (!root.TryGetProperty("tokens", out var tokens))
            {
                failureReason = $"Codex auth file has no 'tokens' object ({path}).";
                return null;
            }

            var access = GetString(tokens, "access_token");
            if (string.IsNullOrWhiteSpace(access))
            {
                failureReason = $"Codex auth file has no 'tokens.access_token' ({path}).";
                return null;
            }

            var refresh = GetString(tokens, "refresh_token") ?? string.Empty;
            var idToken = GetString(tokens, "id_token");
            var accountId = GetString(tokens, "account_id");
            var lastRefresh = IsoDate.Parse(GetString(root, "last_refresh"));

            failureReason = null;
            return new CodexOAuthCredentials
            {
                AccessToken = access,
                RefreshToken = refresh,
                IdToken = idToken,
                AccountId = accountId,
                LastRefresh = lastRefresh
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            failureReason = $"Could not read Codex auth file {path}: {ex.GetType().Name}: {ex.Message}";
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
            throw ProviderFetchException.FromResponse(
                "Codex refresh token expired. Run `codex` to log in again.",
                response,
                body,
                ("last-refresh", credentials.LastRefresh?.ToString("O") ?? "never"));
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ProviderFetchException.FromResponse(
                $"Codex token refresh failed ({(int)response.StatusCode}).",
                response,
                body);
        }

        using var doc = ProviderJson.Parse(body, response, "Codex token refresh returned malformed JSON.");
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

internal static class ProviderJson
{
    public static JsonDocument Parse(string body, HttpResponseMessage response, string message)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            throw ProviderFetchException.FromResponse(message, response, body, ex);
        }
    }

    public static T Deserialize<T>(string body, HttpResponseMessage response, string message)
        where T : new()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body) ?? new T();
        }
        catch (JsonException ex)
        {
            throw ProviderFetchException.FromResponse(message, response, body, ex);
        }
    }
}

internal sealed class CodexUsageResponse
{
    [JsonPropertyName("rate_limit")]
    public CodexRateLimit? RateLimit { get; set; }
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
            throw ProviderFetchException.FromResponse(
                $"Codex OAuth API failed ({(int)response.StatusCode}).",
                response,
                json,
                ("account-id", string.IsNullOrWhiteSpace(accountId) ? "not sent" : accountId));
        }

        return ProviderJson.Deserialize<CodexUsageResponse>(json, response, "Codex OAuth API returned malformed JSON.");
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
            throw ProviderFetchException.FromResponse(
                $"Codex web usage failed ({(int)response.StatusCode}).",
                response,
                json);
        }

        return ProviderJson.Deserialize<CodexUsageResponse>(json, response, "Codex web usage returned malformed JSON.");
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
}

internal static class CodexCliClient
{
    public static async Task<CodexCliResult?> FetchAsync(
        SourceDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var rpcResult = await TryFetchViaRpcAsync(diagnostics, cancellationToken);
        if (rpcResult != null)
        {
            return rpcResult;
        }

        return await TryFetchViaPtyAsync(diagnostics, cancellationToken);
    }

    private static async Task<CodexCliResult?> TryFetchViaRpcAsync(
        SourceDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        using var client = new JsonRpcProcessClient("codex", "-s read-only -a untrusted app-server");

        try
        {
            await client.StartAsync(cancellationToken);

            await client.SendAsync(1, "initialize", new { client = new { name = "WinCodexBar", version = "0.1" } }, cancellationToken)
                ;

            var rateLimits = await client.SendAsync(3, "account/rateLimits/read", null, cancellationToken);

            var primary = ParseRateWindow(rateLimits, "primary_window", "primaryWindow", "primary");
            var secondary = ParseRateWindow(rateLimits, "secondary_window", "secondaryWindow", "secondary");

            return new CodexCliResult
            {
                SourceLabel = "codex-cli",
                Primary = primary,
                Secondary = secondary
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            diagnostics.Note($"codex app-server RPC failed: {ex.GetType().Name}: {ex.Message}");
            diagnostics.Note("codex-rpc-stderr", DiagnosticsDetail.Body(client.StandardError));
            return null;
        }
    }

    private static async Task<CodexCliResult?> TryFetchViaPtyAsync(
        SourceDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var run = await ProcessRunner.RunInteractiveAsync(
            "codex",
            "",
            "/status\n",
            TimeSpan.FromSeconds(8),
            cancellationToken);

        diagnostics.Note("codex-pty", run.Describe());

        if (string.IsNullOrWhiteSpace(run.Output))
        {
            diagnostics.Note("codex-pty-stderr", DiagnosticsDetail.Body(run.StandardError));
            return null;
        }

        var sessionPercent = ExtractPercent(run.Output, "5h", "5 h", "5-hour", "5 hour");
        var weeklyPercent = ExtractPercent(run.Output, "weekly", "week");

        if (sessionPercent == null && weeklyPercent == null)
        {
            diagnostics.Note("No usage percentages found in `codex /status` output.");
            diagnostics.Note("codex-pty-output", DiagnosticsDetail.Body(run.Output));
        }

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
    private readonly StringBuilder _standardError = new();
    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;

    public JsonRpcProcessClient(string fileName, string arguments)
    {
        _fileName = fileName;
        _arguments = arguments;
    }

    public string StandardError
    {
        get
        {
            lock (_standardError)
            {
                return _standardError.ToString().Trim();
            }
        }
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

        // Drained on a callback so stderr is available without blocking the stdout reader.
        _process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data == null)
            {
                return;
            }

            lock (_standardError)
            {
                _standardError.AppendLine(args.Data);
            }
        };

        try
        {
            _process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ProviderFetchException(
                $"Could not start '{_fileName}'. Is it installed and on PATH?",
                DiagnosticsDetail.Compose(
                    ("command", $"{_fileName} {_arguments}"),
                    ("native-error", ex.NativeErrorCode.ToString())),
                ex);
        }

        _process.BeginErrorReadLine();
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
        while (true)
        {
            var (timedOut, line) = await DeadlineReader.ReadLineAsync(
                _stdout,
                timeoutAt - DateTimeOffset.Now,
                cancellationToken);

            if (timedOut || line == null)
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
            // ignored
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
    public static string CredentialsPath => ResolveCredentialsPath();

    public static ClaudeOAuthCredentials? Load(out string? failureReason)
    {
        var path = ResolveCredentialsPath();
        if (!File.Exists(path))
        {
            failureReason = $"Claude credentials file not found at {path}. Run `claude` to log in.";
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!TryGetChild(root, out var oauth, "claudeAiOauth", "claude_ai_oauth"))
            {
                failureReason = $"Claude credentials file has no 'claudeAiOauth' object ({path}).";
                return null;
            }

            var accessToken = GetString(oauth, "accessToken") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                failureReason = $"Claude credentials file has no 'claudeAiOauth.accessToken' ({path}).";
                return null;
            }

            var expiresAt = GetDouble(oauth, "expiresAt");
            var scopes = GetStringArray(oauth, "scopes");
            var rateLimitTier = GetString(oauth, "rateLimitTier");

            failureReason = null;
            return new ClaudeOAuthCredentials
            {
                AccessToken = accessToken,
                ExpiresAt = expiresAt.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds((long)expiresAt.Value) : null,
                Scopes = scopes,
                RateLimitTier = rateLimitTier
            };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            failureReason = $"Could not read Claude credentials file {path}: {ex.GetType().Name}: {ex.Message}";
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
    public DateTimeOffset? ResetsAt => IsoDate.Parse(ResetsAtRaw);
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
        request.Headers.Add("anthropic-beta", BetaHeader);
        request.Headers.Add("User-Agent", "WinCodexBar");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ProviderFetchException.FromResponse(
                $"Claude OAuth API failed ({(int)response.StatusCode}).",
                response,
                json,
                ("anthropic-beta", BetaHeader));
        }

        return ProviderJson.Deserialize<ClaudeOAuthUsageResponse>(json, response, "Claude OAuth API returned malformed JSON.");
    }
}

internal sealed class ClaudeWebUsageResult
{
    public double SessionPercentUsed { get; init; }
    public DateTimeOffset? SessionResetsAt { get; init; }
    public double? WeeklyPercentUsed { get; init; }
    public DateTimeOffset? WeeklyResetsAt { get; init; }
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
        return new ClaudeWebUsageResult
        {
            SessionPercentUsed = usage.SessionPercentUsed,
            SessionResetsAt = usage.SessionResetsAt,
            WeeklyPercentUsed = usage.WeeklyPercentUsed,
            WeeklyResetsAt = usage.WeeklyResetsAt
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
            throw ProviderFetchException.FromResponse(
                $"Claude org lookup failed ({(int)response.StatusCode}).",
                response,
                json);
        }

        var orgs = ProviderJson.Deserialize<List<ClaudeOrgResponse>>(json, response, "Claude org lookup returned malformed JSON.");
        var selected = orgs.Find(org => org.Capabilities?.Contains("chat", StringComparer.OrdinalIgnoreCase) == true)
            ?? orgs.Find(org => org.Capabilities?.Contains("api", StringComparer.OrdinalIgnoreCase) != true)
            ?? (orgs.Count > 0 ? orgs[0] : null);

        if (selected == null || string.IsNullOrWhiteSpace(selected.Uuid))
        {
            throw ProviderFetchException.FromResponse(
                "No Claude organization found.",
                response,
                json,
                ("organizations-returned", orgs.Count.ToString()));
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
            throw ProviderFetchException.FromResponse(
                $"Claude usage failed ({(int)response.StatusCode}).",
                response,
                json);
        }

        using var doc = ProviderJson.Parse(json, response, "Claude usage returned malformed JSON.");
        var root = doc.RootElement;
        var fiveHour = root.TryGetProperty("five_hour", out var session) ? session : default;
        var sevenDay = root.TryGetProperty("seven_day", out var weekly) ? weekly : default;

        var sessionUtil = GetDouble(fiveHour, "utilization") ?? 0;
        var sessionResetsAt = IsoDate.Parse(GetString(fiveHour, "resets_at"));
        var weeklyUtil = GetDouble(sevenDay, "utilization");
        var weeklyResetsAt = IsoDate.Parse(GetString(sevenDay, "resets_at"));

        return new ClaudeWebUsagePayload
        {
            SessionPercentUsed = sessionUtil,
            SessionResetsAt = sessionResetsAt,
            WeeklyPercentUsed = weeklyUtil,
            WeeklyResetsAt = weeklyResetsAt
        };
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

}

internal sealed class ClaudeCliResult
{
    public string SourceLabel { get; init; } = "claude-cli";
    public UsageWindow? Primary { get; init; }
    public UsageWindow? Secondary { get; init; }
}

internal static class ClaudeCliClient
{
    public static async Task<ClaudeCliResult?> FetchAsync(
        SourceDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var run = await ProcessRunner.RunInteractiveAsync(
            "claude",
            "--allowed-tools \"\"",
            "/usage\n",
            TimeSpan.FromSeconds(10),
            cancellationToken);

        diagnostics.Note("claude-cli", run.Describe());

        if (string.IsNullOrWhiteSpace(run.Output))
        {
            diagnostics.Note("claude-cli-stderr", DiagnosticsDetail.Body(run.StandardError));
            return null;
        }

        var sessionPercent = ExtractPercent(run.Output, "current session", "session");
        var weeklyPercent = ExtractPercent(run.Output, "current week", "weekly", "week");

        if (sessionPercent == null && weeklyPercent == null)
        {
            diagnostics.Note("No usage percentages found in `claude /usage` output.");
            diagnostics.Note("claude-cli-output", DiagnosticsDetail.Body(run.Output));
        }

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

internal sealed record ProcessRunResult(
    string Command,
    string? Output,
    string? StandardError,
    int? ExitCode,
    bool TimedOut,
    TimeSpan Elapsed)
{
    public string Describe()
    {
        var exit = ExitCode.HasValue ? ExitCode.Value.ToString() : "n/a";
        var state = TimedOut ? "timed out" : "exited";
        return $"`{Command}` {state} after {Elapsed.TotalMilliseconds:0}ms (exit {exit}, {Output?.Length ?? 0} bytes stdout)";
    }
}

// A child process that neither writes nor exits — one sitting on a login prompt, say —
// would block ReadLineAsync forever, so every read races the caller's remaining time.
internal static class DeadlineReader
{
    public static async Task<(bool TimedOut, string? Line)> ReadLineAsync(
        StreamReader reader,
        TimeSpan remaining,
        CancellationToken cancellationToken)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return (true, null);
        }

        var readTask = reader.ReadLineAsync(cancellationToken).AsTask();
        if (await Task.WhenAny(readTask, Task.Delay(remaining, cancellationToken)) != readTask)
        {
            return (true, null);
        }

        return (false, await readTask);
    }
}

internal static class ProcessRunner
{
    public static async Task<ProcessRunResult> RunInteractiveAsync(
        string fileName,
        string arguments,
        string input,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var command = string.IsNullOrWhiteSpace(arguments) ? fileName : $"{fileName} {arguments}";
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessRunResult(command, null, null, null, false, TimeSpan.Zero);
            }
        }
        catch (Win32Exception ex)
        {
            throw new ProviderFetchException(
                $"Could not start '{fileName}'. Is it installed and on PATH?",
                DiagnosticsDetail.Compose(
                    ("command", command),
                    ("native-error", ex.NativeErrorCode.ToString()),
                    ("path", Environment.GetEnvironmentVariable("PATH"))),
                ex);
        }

        await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync();

        var output = new StringBuilder();
        var started = DateTimeOffset.Now;
        var timedOut = false;

        while (!process.HasExited)
        {
            var (readTimedOut, line) = await DeadlineReader.ReadLineAsync(
                process.StandardOutput,
                timeout - (DateTimeOffset.Now - started),
                cancellationToken);

            if (readTimedOut)
            {
                timedOut = true;
                break;
            }

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
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process raced us to exit; nothing to clean up.
        }

        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        var text = output.ToString().Trim();

        return new ProcessRunResult(
            command,
            string.IsNullOrWhiteSpace(text) ? null : text,
            string.IsNullOrWhiteSpace(stderr) ? null : stderr.Trim(),
            process.HasExited ? process.ExitCode : null,
            timedOut,
            DateTimeOffset.Now - started);
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


