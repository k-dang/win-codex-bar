using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class ClaudeProviderUsageFetcher : IProviderUsageFetcher
{
    private readonly HttpClient _httpClient;
    private readonly IDiagnosticsLogger? _logger;

    public ClaudeProviderUsageFetcher(HttpClient httpClient, IDiagnosticsLogger? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    public ProviderKind Kind => ProviderKind.Claude;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        AppSettings appSettings,
        ProviderSettings providerSettings,
        CancellationToken cancellationToken)
    {
        providerSettings ??= ProviderSettings.CreateDefault(ProviderKind.Claude);

        var errors = new List<string>();
        var sources = ResolveSourceOrder(providerSettings.SourceMode);

        foreach (var source in sources)
        {
            var sourceName = source.ToString();
            _logger?.LogAttempt(ProviderKind.Claude, sourceName, $"Attempting {sourceName} fetch...");
            var sw = Stopwatch.StartNew();

            try
            {
                var snapshot = source switch
                {
                    ProviderSourceMode.OAuth => await TryFetchClaudeOAuthAsync(cancellationToken),
                    ProviderSourceMode.Web => await TryFetchClaudeWebAsync(providerSettings, cancellationToken),
                    ProviderSourceMode.Cli => await TryFetchClaudeCliAsync(cancellationToken),
                    _ => null
                };

                sw.Stop();

                if (snapshot != null)
                {
                    _logger?.LogSuccess(ProviderKind.Claude, sourceName, $"Successfully fetched via {sourceName}", sw.Elapsed);
                    return snapshot;
                }

                _logger?.LogFailure(ProviderKind.Claude, sourceName, $"No data from {sourceName}", sw.Elapsed);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger?.LogFailure(ProviderKind.Claude, sourceName, ex.Message, sw.Elapsed);
                errors.Add(ex.Message);
            }
        }

        return new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Claude,
            SourceLabel = SourceLabel(providerSettings.SourceMode),
            Error = errors.Count == 0 ? "No Claude usage sources available." : string.Join(" ", errors),
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

    private async Task<ProviderUsageSnapshot?> TryFetchClaudeOAuthAsync(CancellationToken cancellationToken)
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
            AccountPlan = credentials.RateLimitTier,
            Primary = ToWindow(usage.FiveHour, "Session", 5 * 60),
            Secondary = ToWindow(usage.SevenDay, "Weekly", 7 * 24 * 60),
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private async Task<ProviderUsageSnapshot?> TryFetchClaudeWebAsync(ProviderSettings settings, CancellationToken cancellationToken)
    {
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
            AccountEmail = usage.AccountEmail,
            Primary = new UsageWindow
            {
                Label = "Session",
                UsedPercent = usage.SessionPercentUsed,
                WindowMinutes = 5 * 60,
                ResetsAt = usage.SessionResetsAt,
                ResetDescription = UsageWindowFormatter.FormatResetDescription(usage.SessionResetsAt)
            },
            Secondary = usage.WeeklyPercentUsed.HasValue ? new UsageWindow
            {
                Label = "Weekly",
                UsedPercent = usage.WeeklyPercentUsed,
                WindowMinutes = 7 * 24 * 60,
                ResetsAt = usage.WeeklyResetsAt,
                ResetDescription = UsageWindowFormatter.FormatResetDescription(usage.WeeklyResetsAt)
            } : null,
            CreditsText = usage.ExtraUsageText,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private async Task<ProviderUsageSnapshot?> TryFetchClaudeCliAsync(CancellationToken cancellationToken)
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
            AccountEmail = result.AccountEmail,
            AccountPlan = result.AccountPlan,
            Primary = result.Primary,
            Secondary = result.Secondary,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private static UsageWindow? ToWindow(ClaudeOAuthWindow? window, string label, int windowMinutes)
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
