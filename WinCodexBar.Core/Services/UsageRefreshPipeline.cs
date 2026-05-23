using System.Diagnostics;
using WinCodexBar.Core.Models;

namespace WinCodexBar.Core.Services;

public sealed class UsageRefreshPipeline : IUsageRefreshPipeline
{
    private readonly IReadOnlyDictionary<(ProviderKind Provider, ProviderSourceMode Source), IProviderUsageSource> _sources;
    private readonly IDiagnosticsLogger? _logger;
    private readonly IUsageClock _clock;

    internal UsageRefreshPipeline(
        IEnumerable<IProviderUsageSource> sources,
        IDiagnosticsLogger? logger = null,
        IUsageClock? clock = null)
    {
        _sources = sources.ToDictionary(source => (source.Provider, source.SourceMode));
        _logger = logger;
        _clock = clock ?? SystemUsageClock.Instance;
    }

    public static UsageRefreshPipeline CreateDefault(HttpClient? httpClient = null, IDiagnosticsLogger? logger = null)
    {
        var client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        return new UsageRefreshPipeline(
            ProviderUsageSourceFactory.CreateDefault(client),
            logger);
    }

    public async Task<UsageSummary> RefreshAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _logger?.LogRefreshStarted();
        var stopwatch = _clock.StartStopwatch();

        try
        {
            var summary = new UsageSummary
            {
                LastUpdated = _clock.Now
            };

            foreach (var provider in settings.EnumerateProviders().Select(entry => entry.Key).OrderBy(provider => provider))
            {
                var snapshot = await FetchProviderCoreAsync(settings, provider, cancellationToken);
                if (snapshot != null)
                {
                    summary.ProviderSnapshots.Add(snapshot);
                }
            }

            return summary;
        }
        finally
        {
            stopwatch.Stop();
            _logger?.LogRefreshCompleted(stopwatch.Elapsed);
        }
    }

    public async Task<UsageSummary> RefreshProviderAsync(
        AppSettings settings,
        UsageSummary currentSummary,
        ProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(currentSummary);

        var snapshot = await FetchProviderAsync(settings, provider, cancellationToken);
        if (snapshot == null)
        {
            return currentSummary;
        }

        var summary = new UsageSummary
        {
            LastUpdated = _clock.Now
        };

        foreach (var existing in currentSummary.ProviderSnapshots.Where(item => item.Provider != provider))
        {
            summary.ProviderSnapshots.Add(existing);
        }

        summary.ProviderSnapshots.Add(snapshot);
        return summary;
    }

    private async Task<ProviderUsageSnapshot?> FetchProviderAsync(
        AppSettings settings,
        ProviderKind provider,
        CancellationToken cancellationToken)
    {
        var providerSettings = settings.GetProviderSettings(provider);
        if (!providerSettings.Enabled)
        {
            return null;
        }

        _logger?.LogRefreshStarted();
        var stopwatch = _clock.StartStopwatch();

        try
        {
            return await FetchProviderCoreAsync(settings, provider, cancellationToken);
        }
        finally
        {
            stopwatch.Stop();
            _logger?.LogRefreshCompleted(stopwatch.Elapsed);
        }
    }

    private async Task<ProviderUsageSnapshot?> FetchProviderCoreAsync(
        AppSettings settings,
        ProviderKind provider,
        CancellationToken cancellationToken)
    {
        var providerSettings = settings.GetProviderSettings(provider);
        if (!providerSettings.Enabled)
        {
            return null;
        }

        var errors = new List<string>();
        var attemptedAny = false;

        foreach (var sourceMode in ResolveSourceOrder(providerSettings.SourceMode))
        {
            if (!_sources.TryGetValue((provider, sourceMode), out var source))
            {
                continue;
            }

            attemptedAny = true;
            var sourceName = sourceMode.ToString();
            _logger?.LogAttempt(provider, sourceName, $"Attempting {sourceName} fetch...");
            var stopwatch = _clock.StartStopwatch();

            try
            {
                var snapshot = await source.FetchAsync(
                    new ProviderUsageSourceRequest(provider, providerSettings, settings),
                    cancellationToken);

                stopwatch.Stop();

                if (snapshot != null)
                {
                    _logger?.LogSuccess(provider, sourceName, $"Successfully fetched via {sourceName}", stopwatch.Elapsed);
                    return snapshot;
                }

                _logger?.LogFailure(provider, sourceName, $"No data from {sourceName}", stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger?.LogFailure(provider, sourceName, ex.Message, stopwatch.Elapsed);
                errors.Add(ex.Message);
            }
        }

        if (!attemptedAny)
        {
            return CreateErrorSnapshot(provider, "No provider usage sources configured.");
        }

        return CreateErrorSnapshot(
            provider,
            errors.Count == 0 ? $"No {provider} usage sources available." : string.Join(" ", errors),
            SourceLabel(providerSettings.SourceMode));
    }

    private ProviderUsageSnapshot CreateErrorSnapshot(ProviderKind provider, string error, string sourceLabel = "auto")
    {
        return new ProviderUsageSnapshot
        {
            Provider = provider,
            SourceLabel = sourceLabel,
            Error = string.IsNullOrWhiteSpace(error) ? $"Failed to fetch {provider} usage." : error,
            UpdatedAt = _clock.Now
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
}

internal interface IProviderUsageSource
{
    ProviderKind Provider { get; }
    ProviderSourceMode SourceMode { get; }

    Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken);
}

internal sealed record ProviderUsageSourceRequest(
    ProviderKind Provider,
    ProviderSettings ProviderSettings,
    AppSettings AppSettings);

internal interface IUsageClock
{
    DateTimeOffset Now { get; }
    IUsageStopwatch StartStopwatch();
}

internal interface IUsageStopwatch
{
    TimeSpan Elapsed { get; }
    void Stop();
}

internal sealed class SystemUsageClock : IUsageClock
{
    public static SystemUsageClock Instance { get; } = new();

    public DateTimeOffset Now => DateTimeOffset.Now;

    public IUsageStopwatch StartStopwatch()
    {
        return new SystemUsageStopwatch(Stopwatch.StartNew());
    }

    private sealed class SystemUsageStopwatch : IUsageStopwatch
    {
        private readonly Stopwatch _stopwatch;

        public SystemUsageStopwatch(Stopwatch stopwatch)
        {
            _stopwatch = stopwatch;
        }

        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public void Stop()
        {
            _stopwatch.Stop();
        }
    }
}
