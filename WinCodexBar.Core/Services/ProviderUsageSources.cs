using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

internal static class ProviderUsageSourceFactory
{
    public static IReadOnlyList<IProviderUsageSource> CreateDefault(HttpClient httpClient)
    {
        return new IProviderUsageSource[]
        {
            new CodexOAuthUsageSource(httpClient),
            new CodexWebUsageSource(httpClient),
            new CodexCliUsageSource(),
            new ClaudeOAuthUsageSource(httpClient),
            new ClaudeWebUsageSource(httpClient),
            new ClaudeCliUsageSource(),
            new CursorAppTokenUsageSource(httpClient),
            new CursorWebUsageSource(httpClient)
        };
    }
}

internal sealed class CodexOAuthUsageSource : IProviderUsageSource
{
    private readonly HttpClient _httpClient;

    public CodexOAuthUsageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ProviderKind Provider => ProviderKind.Codex;
    public ProviderSourceMode SourceMode => ProviderSourceMode.OAuth;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken)
    {
        var credentials = CodexOAuthCredentialsStore.Load(out var loadFailure);
        if (credentials == null)
        {
            request.Diagnostics.Note(loadFailure ?? "No Codex OAuth credentials available.");
            return null;
        }

        request.Diagnostics.Note("credentials", CodexOAuthCredentialsStore.AuthPath);
        request.Diagnostics.Note("last-refresh", credentials.LastRefresh?.ToString("O") ?? "never");

        if (credentials.NeedsRefresh && !string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
            request.Diagnostics.Note("Access token is stale; refreshing before fetching usage.");
            credentials = await CodexTokenRefresher.RefreshAsync(_httpClient, credentials, cancellationToken);
            CodexOAuthCredentialsStore.Save(credentials);
        }

        var usage = await CodexOAuthUsageFetcher.FetchUsageAsync(
            _httpClient,
            credentials.AccessToken,
            credentials.AccountId,
            cancellationToken);

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Codex,
            SourceLabel = "oauth",
            Primary = CodexUsageMapper.ToWindow(usage.RateLimit?.PrimaryWindow, "Session"),
            Secondary = CodexUsageMapper.ToWindow(usage.RateLimit?.SecondaryWindow, "Weekly"),
            UpdatedAt = DateTimeOffset.Now
        };
    }
}

// Web sources share the rule that manual-cookie mode must be selected and populated
// before any request is attempted; a null fall-through lets the pipeline try other sources.
internal abstract class ManualCookieUsageSource : IProviderUsageSource
{
    protected ManualCookieUsageSource(HttpClient httpClient)
    {
        HttpClient = httpClient;
    }

    protected HttpClient HttpClient { get; }

    public abstract ProviderKind Provider { get; }
    public ProviderSourceMode SourceMode => ProviderSourceMode.Web;

    public Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken)
    {
        var settings = request.ProviderSettings;
        if (settings.CookieSource != CookieSourceMode.Manual)
        {
            request.Diagnostics.Note($"Web source needs a manual cookie, but cookie source is {settings.CookieSource}.");
            return Task.FromResult<ProviderUsageSnapshot?>(null);
        }

        if (string.IsNullOrWhiteSpace(settings.CookieHeader))
        {
            request.Diagnostics.Note("Web source is set to manual cookie, but the cookie header is empty.");
            return Task.FromResult<ProviderUsageSnapshot?>(null);
        }

        request.Diagnostics.Note("cookie-names", DescribeCookieNames(settings.CookieHeader));
        return FetchWithCookieAsync(settings.CookieHeader, request.Diagnostics, cancellationToken);
    }

    protected abstract Task<ProviderUsageSnapshot?> FetchWithCookieAsync(
        string cookieHeader,
        SourceDiagnostics diagnostics,
        CancellationToken cancellationToken);

    // Names only; the values are the secret.
    private static string DescribeCookieNames(string cookieHeader)
    {
        var names = CookieHeaderParser.Parse(cookieHeader).Select(pair => pair.Name).ToArray();
        return names.Length == 0 ? "none parsed" : string.Join(", ", names);
    }
}

internal sealed class CodexWebUsageSource : ManualCookieUsageSource
{
    public CodexWebUsageSource(HttpClient httpClient)
        : base(httpClient)
    {
    }

    public override ProviderKind Provider => ProviderKind.Codex;

    protected override async Task<ProviderUsageSnapshot?> FetchWithCookieAsync(
        string cookieHeader,
        SourceDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var usage = await CodexOAuthUsageFetcher.FetchUsageWithCookiesAsync(
            HttpClient,
            cookieHeader,
            cancellationToken);

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Codex,
            SourceLabel = "web",
            Primary = CodexUsageMapper.ToWindow(usage.RateLimit?.PrimaryWindow, "Session"),
            Secondary = CodexUsageMapper.ToWindow(usage.RateLimit?.SecondaryWindow, "Weekly"),
            UpdatedAt = DateTimeOffset.Now
        };
    }
}

internal sealed class CodexCliUsageSource : IProviderUsageSource
{
    public ProviderKind Provider => ProviderKind.Codex;
    public ProviderSourceMode SourceMode => ProviderSourceMode.Cli;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await CodexCliClient.FetchAsync(request.Diagnostics, cancellationToken);
        if (result == null)
        {
            return null;
        }

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Codex,
            SourceLabel = result.SourceLabel,
            Primary = result.Primary,
            Secondary = result.Secondary,
            UpdatedAt = DateTimeOffset.Now
        };
    }
}

internal sealed class ClaudeOAuthUsageSource : IProviderUsageSource
{
    private readonly HttpClient _httpClient;

    public ClaudeOAuthUsageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ProviderKind Provider => ProviderKind.Claude;
    public ProviderSourceMode SourceMode => ProviderSourceMode.OAuth;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken)
    {
        var credentials = ClaudeOAuthCredentialsStore.Load(out var loadFailure);
        if (credentials == null)
        {
            request.Diagnostics.Note(loadFailure ?? "No Claude OAuth credentials available.");
            return null;
        }

        request.Diagnostics.Note("credentials", ClaudeOAuthCredentialsStore.CredentialsPath);
        request.Diagnostics.Note("scopes", credentials.Scopes.Count == 0 ? "none" : string.Join(" ", credentials.Scopes));

        if (credentials.IsExpired)
        {
            request.Diagnostics.Note(
                $"Claude OAuth token expired at {credentials.ExpiresAt:O}. Run `claude` to sign in again.");
            return null;
        }

        var usage = await ClaudeOAuthUsageFetcher.FetchUsageAsync(
            _httpClient,
            credentials.AccessToken,
            cancellationToken);

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Claude,
            SourceLabel = "oauth",
            Primary = ClaudeUsageMapper.ToWindow(usage.FiveHour, "Session", 5 * 60),
            Secondary = ClaudeUsageMapper.ToWindow(usage.SevenDay, "Weekly", 7 * 24 * 60),
            UpdatedAt = DateTimeOffset.Now
        };
    }
}

internal sealed class ClaudeWebUsageSource : ManualCookieUsageSource
{
    public ClaudeWebUsageSource(HttpClient httpClient)
        : base(httpClient)
    {
    }

    public override ProviderKind Provider => ProviderKind.Claude;

    protected override async Task<ProviderUsageSnapshot?> FetchWithCookieAsync(
        string cookieHeader,
        SourceDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var usage = await ClaudeWebApiFetcher.FetchUsageAsync(
            HttpClient,
            cookieHeader,
            cancellationToken);

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Claude,
            SourceLabel = "web",
            Primary = new UsageWindow
            {
                Label = "Session",
                UsedPercent = usage.SessionPercentUsed,
                WindowMinutes = 5 * 60,
                ResetsAt = usage.SessionResetsAt,
                ResetDescription = UsageWindowFormatter.FormatResetDescription(usage.SessionResetsAt)
            },
            Secondary = usage.WeeklyPercentUsed.HasValue
                ? new UsageWindow
                {
                    Label = "Weekly",
                    UsedPercent = usage.WeeklyPercentUsed,
                    WindowMinutes = 7 * 24 * 60,
                    ResetsAt = usage.WeeklyResetsAt,
                    ResetDescription = UsageWindowFormatter.FormatResetDescription(usage.WeeklyResetsAt)
                }
                : null,
            UpdatedAt = DateTimeOffset.Now
        };
    }
}

internal sealed class ClaudeCliUsageSource : IProviderUsageSource
{
    public ProviderKind Provider => ProviderKind.Claude;
    public ProviderSourceMode SourceMode => ProviderSourceMode.Cli;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await ClaudeCliClient.FetchAsync(request.Diagnostics, cancellationToken);
        if (result == null)
        {
            return null;
        }

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Claude,
            SourceLabel = result.SourceLabel,
            Primary = result.Primary,
            Secondary = result.Secondary,
            UpdatedAt = DateTimeOffset.Now
        };
    }
}

internal sealed class CursorAppTokenUsageSource : IProviderUsageSource
{
    private readonly HttpClient _httpClient;
    private readonly ICursorLocalTokenReader _tokenReader;

    public CursorAppTokenUsageSource(HttpClient httpClient, ICursorLocalTokenReader? tokenReader = null)
    {
        _httpClient = httpClient;
        _tokenReader = tokenReader ?? new CursorStateDbTokenReader();
    }

    public ProviderKind Provider => ProviderKind.Cursor;
    public ProviderSourceMode SourceMode => ProviderSourceMode.OAuth;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken)
    {
        // The SQLite read is synchronous file I/O and the pipeline is awaited on the
        // UI thread, so push it off the caller's thread.
        var token = await Task.Run(_tokenReader.ReadAccessToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            request.Diagnostics.Note(
                $"No Cursor access token in the app state database ({CursorStateDbTokenReader.StateDbPath}). " +
                "Sign in to the Cursor app, or switch this provider to a manual cookie.");
            return null;
        }

        var session = CursorAppSession.TryCreate(token, DateTimeOffset.UtcNow);
        if (session == null)
        {
            request.Diagnostics.Note(
                "Cursor app token is malformed or expired (no usable 'sub'/'exp' claim). Sign in to the Cursor app again.");
            return null;
        }

        var usage = await CursorWebApiFetcher.FetchUsageAsync(
            _httpClient,
            session.CookieHeader,
            session.UserId,
            request.Diagnostics,
            cancellationToken);

        return CursorUsageMapper.ToSnapshot(usage, "app");
    }
}

internal sealed class CursorWebUsageSource : ManualCookieUsageSource
{
    public CursorWebUsageSource(HttpClient httpClient)
        : base(httpClient)
    {
    }

    public override ProviderKind Provider => ProviderKind.Cursor;

    protected override async Task<ProviderUsageSnapshot?> FetchWithCookieAsync(
        string cookieHeader,
        SourceDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        var usage = await CursorWebApiFetcher.FetchUsageAsync(
            HttpClient,
            cookieHeader,
            knownUserId: null,
            diagnostics,
            cancellationToken);

        return CursorUsageMapper.ToSnapshot(usage, "web");
    }
}

internal static class CodexUsageMapper
{
    public static UsageWindow? ToWindow(CodexUsageWindow? window, string label)
    {
        if (window == null)
        {
            return null;
        }

        var resetsAt = window.ResetAtUnixSeconds.HasValue
            ? DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds.Value)
            : (DateTimeOffset?)null;

        return new UsageWindow
        {
            Label = label,
            UsedPercent = window.UsedPercent,
            WindowMinutes = window.WindowMinutes,
            ResetsAt = resetsAt,
            ResetDescription = UsageWindowFormatter.FormatResetDescription(resetsAt)
        };
    }
}

internal static class ClaudeUsageMapper
{
    public static UsageWindow? ToWindow(ClaudeOAuthWindow? window, string label, int windowMinutes)
    {
        if (window == null)
        {
            return null;
        }

        return new UsageWindow
        {
            Label = label,
            UsedPercent = window.Utilization,
            WindowMinutes = windowMinutes,
            ResetsAt = window.ResetsAt,
            ResetDescription = UsageWindowFormatter.FormatResetDescription(window.ResetsAt)
        };
    }
}
