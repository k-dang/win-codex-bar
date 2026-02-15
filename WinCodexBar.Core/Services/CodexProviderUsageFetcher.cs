using System.Diagnostics;
using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

public sealed class CodexProviderUsageFetcher : IProviderUsageFetcher
{
    private readonly HttpClient _httpClient;
    private readonly IDiagnosticsLogger? _logger;

    public CodexProviderUsageFetcher(HttpClient httpClient, IDiagnosticsLogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    public ProviderKind Kind => ProviderKind.Codex;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        AppSettings appSettings,
        ProviderSettings providerSettings,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var sources = ResolveSourceOrder(providerSettings.SourceMode);

        foreach (var source in sources)
        {
            var sourceName = source.ToString();
            _logger?.LogAttempt(ProviderKind.Codex, sourceName, $"Attempting {sourceName} fetch...");
            var sw = Stopwatch.StartNew();

            try
            {
                var snapshot = source switch
                {
                    ProviderSourceMode.OAuth => await TryFetchCodexOAuthAsync(cancellationToken),
                    ProviderSourceMode.Web => await TryFetchCodexWebAsync(providerSettings, cancellationToken),
                    ProviderSourceMode.Cli => await TryFetchCodexCliAsync(cancellationToken),
                    _ => null
                };

                sw.Stop();

                if (snapshot != null)
                {
                    _logger?.LogSuccess(ProviderKind.Codex, sourceName, $"Successfully fetched via {sourceName}", sw.Elapsed);
                    return snapshot;
                }

                _logger?.LogFailure(ProviderKind.Codex, sourceName, $"No data from {sourceName}", sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger?.LogFailure(ProviderKind.Codex, sourceName, ex.Message, sw.Elapsed);
                errors.Add(ex.Message);
            }
        }

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Codex,
            SourceLabel = SourceLabel(providerSettings.SourceMode),
            Error = errors.Count == 0 ? "No Codex usage sources available." : string.Join(" ", errors),
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static ProviderSourceMode[] ResolveSourceOrder(ProviderSourceMode mode)
    {
        return mode switch
        {
            ProviderSourceMode.OAuth => new[] { ProviderSourceMode.OAuth },
            ProviderSourceMode.Web => new[] { ProviderSourceMode.Web },
            ProviderSourceMode.Cli => new[] { ProviderSourceMode.Cli },
            _ => new[] { ProviderSourceMode.OAuth, ProviderSourceMode.Web, ProviderSourceMode.Cli }
        };
    }

    private static string SourceLabel(ProviderSourceMode mode)
    {
        return mode switch
        {
            ProviderSourceMode.OAuth => "oauth",
            ProviderSourceMode.Web => "web",
            ProviderSourceMode.Cli => "cli",
            _ => "auto"
        };
    }

    private async Task<ProviderUsageSnapshot?> TryFetchCodexOAuthAsync(CancellationToken cancellationToken)
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

        var primary = usage.RateLimit?.PrimaryWindow;
        var secondary = usage.RateLimit?.SecondaryWindow;

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Codex,
            SourceLabel = "oauth",
            AccountEmail = CodexOAuthCredentialsStore.ResolveEmail(credentials),
            AccountPlan = CodexOAuthCredentialsStore.ResolvePlan(usage, credentials),
            Primary = ToWindow(primary, "Session"),
            Secondary = ToWindow(secondary, "Weekly"),
            CreditsText = usage.Credits?.ToDisplayText(),
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private async Task<ProviderUsageSnapshot?> TryFetchCodexWebAsync(ProviderSettings settings, CancellationToken cancellationToken)
    {
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
            Primary = ToWindow(usage.RateLimit?.PrimaryWindow, "Session"),
            Secondary = ToWindow(usage.RateLimit?.SecondaryWindow, "Weekly"),
            CreditsText = usage.Credits?.ToDisplayText(),
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private async Task<ProviderUsageSnapshot?> TryFetchCodexCliAsync(CancellationToken cancellationToken)
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
            AccountEmail = result.AccountEmail,
            AccountPlan = result.AccountPlan,
            Primary = result.Primary,
            Secondary = result.Secondary,
            CreditsText = result.CreditsText,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static UsageWindow? ToWindow(CodexUsageWindow? window, string label)
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


