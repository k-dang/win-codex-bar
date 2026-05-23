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
            new ClaudeCliUsageSource()
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
        var credentials = CodexOAuthCredentialsStore.Load();
        if (credentials == null)
        {
            return null;
        }

        if (credentials.NeedsRefresh && !string.IsNullOrWhiteSpace(credentials.RefreshToken))
        {
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

internal sealed class CodexWebUsageSource : IProviderUsageSource
{
    private readonly HttpClient _httpClient;

    public CodexWebUsageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ProviderKind Provider => ProviderKind.Codex;
    public ProviderSourceMode SourceMode => ProviderSourceMode.Web;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken)
    {
        var settings = request.ProviderSettings;
        if (settings.CookieSource != CookieSourceMode.Manual || string.IsNullOrWhiteSpace(settings.CookieHeader))
        {
            return null;
        }

        var usage = await CodexOAuthUsageFetcher.FetchUsageWithCookiesAsync(
            _httpClient,
            settings.CookieHeader,
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
        var result = await CodexCliClient.FetchAsync(cancellationToken);
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
        var credentials = ClaudeOAuthCredentialsStore.Load();
        if (credentials == null || credentials.IsExpired)
        {
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

internal sealed class ClaudeWebUsageSource : IProviderUsageSource
{
    private readonly HttpClient _httpClient;

    public ClaudeWebUsageSource(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public ProviderKind Provider => ProviderKind.Claude;
    public ProviderSourceMode SourceMode => ProviderSourceMode.Web;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken)
    {
        var settings = request.ProviderSettings;
        if (settings.CookieSource != CookieSourceMode.Manual || string.IsNullOrWhiteSpace(settings.CookieHeader))
        {
            return null;
        }

        var usage = await ClaudeWebApiFetcher.FetchUsageAsync(
            _httpClient,
            settings.CookieHeader,
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
        var result = await ClaudeCliClient.FetchAsync(cancellationToken);
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
