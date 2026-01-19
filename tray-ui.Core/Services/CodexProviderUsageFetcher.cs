using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class CodexProviderUsageFetcher : IProviderUsageFetcher
{
    private readonly HttpClient _httpClient;

    public CodexProviderUsageFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public ProviderKind Kind => ProviderKind.Codex;

    public async Task<ProviderUsageSnapshot?> FetchAsync(
        AppSettings appSettings,
        ProviderSettings providerSettings,
        CancellationToken cancellationToken)
    {
        providerSettings ??= ProviderSettings.CreateDefault(ProviderKind.Codex);

        var errors = new List<string>();
        var sources = ResolveSourceOrder(providerSettings.SourceMode);

        foreach (var source in sources)
        {
            try
            {
                var snapshot = source switch
                {
                    ProviderSourceMode.OAuth => await TryFetchCodexOAuthAsync(cancellationToken).ConfigureAwait(false),
                    ProviderSourceMode.Web => await TryFetchCodexWebAsync(providerSettings, cancellationToken).ConfigureAwait(false),
                    ProviderSourceMode.Cli => await TryFetchCodexCliAsync(cancellationToken).ConfigureAwait(false),
                    _ => null
                };

                if (snapshot != null)
                {
                    return snapshot;
                }
            }
            catch (Exception ex)
            {
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
            credentials = await CodexTokenRefresher.RefreshAsync(_httpClient, credentials, cancellationToken).ConfigureAwait(false);
            CodexOAuthCredentialsStore.Save(credentials);
        }

        var usage = await CodexOAuthUsageFetcher.FetchUsageAsync(
            _httpClient,
            credentials.AccessToken,
            credentials.AccountId,
            cancellationToken).ConfigureAwait(false);

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
            cancellationToken).ConfigureAwait(false);

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
        var result = await CodexCliClient.FetchAsync(cancellationToken).ConfigureAwait(false);
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
