using WinCodexBar.Core.Models;
using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class ProviderUsageServiceTests
{
    [Fact]
    public async Task FetchAsync_ReturnsSnapshotsInProviderOrder()
    {
        var settings = AppSettings.CreateDefault();
        var service = new ProviderUsageService(
            fetchers:
            [
                new FakeFetcher(ProviderKind.Claude, (_, _, _) => Task.FromResult<ProviderUsageSnapshot?>(new ProviderUsageSnapshot { Provider = ProviderKind.Claude, SourceLabel = "test" })),
                new FakeFetcher(ProviderKind.Codex, (_, _, _) => Task.FromResult<ProviderUsageSnapshot?>(new ProviderUsageSnapshot { Provider = ProviderKind.Codex, SourceLabel = "test" }))
            ]);

        var snapshots = await service.FetchAsync(settings);

        Assert.Collection(
            snapshots,
            snapshot => Assert.Equal(ProviderKind.Codex, snapshot.Provider),
            snapshot => Assert.Equal(ProviderKind.Claude, snapshot.Provider));
    }

    [Fact]
    public async Task FetchAsync_SkipsDisabledProviders()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;

        var codexCalls = 0;
        var claudeCalls = 0;
        var service = new ProviderUsageService(
            fetchers:
            [
                new FakeFetcher(ProviderKind.Codex, (_, _, _) =>
                {
                    codexCalls++;
                    return Task.FromResult<ProviderUsageSnapshot?>(new ProviderUsageSnapshot { Provider = ProviderKind.Codex, SourceLabel = "test" });
                }),
                new FakeFetcher(ProviderKind.Claude, (_, _, _) =>
                {
                    claudeCalls++;
                    return Task.FromResult<ProviderUsageSnapshot?>(new ProviderUsageSnapshot { Provider = ProviderKind.Claude, SourceLabel = "test" });
                })
            ]);

        var snapshots = await service.FetchAsync(settings);

        Assert.Single(snapshots);
        Assert.Equal(ProviderKind.Codex, snapshots[0].Provider);
        Assert.Equal(1, codexCalls);
        Assert.Equal(0, claudeCalls);
    }

    [Fact]
    public async Task FetchAsync_ReturnsErrorSnapshotWhenFetcherIsMissing()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;

        var service = new ProviderUsageService(
            fetchers:
            [
                new FakeFetcher(ProviderKind.Unknown, (_, _, _) => Task.FromResult<ProviderUsageSnapshot?>(null))
            ]);

        var snapshot = Assert.Single(await service.FetchAsync(settings));

        Assert.Equal(ProviderKind.Codex, snapshot.Provider);
        Assert.Equal("auto", snapshot.SourceLabel);
        Assert.Equal("No provider fetcher configured.", snapshot.Error);
    }

    [Fact]
    public async Task FetchAsync_ReturnsNoSourcesErrorWhenFetcherReturnsNull()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;

        var service = new ProviderUsageService(
            fetchers:
            [
                new FakeFetcher(ProviderKind.Codex, (_, _, _) => Task.FromResult<ProviderUsageSnapshot?>(null))
            ]);

        var snapshot = Assert.Single(await service.FetchAsync(settings));

        Assert.Equal(ProviderKind.Codex, snapshot.Provider);
        Assert.Equal("No Codex usage sources available.", snapshot.Error);
    }

    [Fact]
    public async Task FetchAsync_UsesFallbackErrorMessageWhenFetcherThrowsWhitespaceMessage()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;

        var service = new ProviderUsageService(
            fetchers:
            [
                new FakeFetcher(ProviderKind.Codex, (_, _, _) => throw new Exception(" "))
            ]);

        var snapshot = Assert.Single(await service.FetchAsync(settings));

        Assert.Equal("Failed to fetch Codex usage.", snapshot.Error);
    }

    [Fact]
    public async Task FetchProviderAsync_ReturnsNullWithoutLoggingWhenProviderIsDisabled()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Codex).Enabled = false;
        var logger = new RecordingLogger();

        var service = new ProviderUsageService(
            fetchers:
            [
                new FakeFetcher(ProviderKind.Codex, (_, _, _) => Task.FromResult<ProviderUsageSnapshot?>(new ProviderUsageSnapshot { Provider = ProviderKind.Codex, SourceLabel = "test" }))
            ],
            logger: logger);

        var snapshot = await service.FetchProviderAsync(settings, ProviderKind.Codex);

        Assert.Null(snapshot);
        Assert.Empty(logger.Events);
    }

    [Fact]
    public async Task FetchProviderAsync_LogsRefreshLifecycleAroundFetch()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;
        var logger = new RecordingLogger();

        var service = new ProviderUsageService(
            fetchers:
            [
                new FakeFetcher(ProviderKind.Codex, (_, _, _) => Task.FromResult<ProviderUsageSnapshot?>(new ProviderUsageSnapshot { Provider = ProviderKind.Codex, SourceLabel = "test" }))
            ],
            logger: logger);

        var snapshot = await service.FetchProviderAsync(settings, ProviderKind.Codex);

        Assert.NotNull(snapshot);
        Assert.Equal(["refresh-started", "refresh-completed"], logger.Events);
    }

    private sealed class FakeFetcher : IProviderUsageFetcher
    {
        private readonly Func<AppSettings, ProviderSettings, CancellationToken, Task<ProviderUsageSnapshot?>> _fetch;

        public FakeFetcher(ProviderKind kind, Func<AppSettings, ProviderSettings, CancellationToken, Task<ProviderUsageSnapshot?>> fetch)
        {
            Kind = kind;
            _fetch = fetch;
        }

        public ProviderKind Kind { get; }

        public Task<ProviderUsageSnapshot?> FetchAsync(AppSettings appSettings, ProviderSettings providerSettings, CancellationToken cancellationToken)
        {
            return _fetch(appSettings, providerSettings, cancellationToken);
        }
    }

    private sealed class RecordingLogger : IDiagnosticsLogger
    {
        public List<string> Events { get; } = new();

        public event EventHandler<DiagnosticsLogEntry>? EntryAdded
        {
            add { }
            remove { }
        }

        public void LogAttempt(ProviderKind provider, string sourceMethod, string message)
        {
            Events.Add($"attempt:{provider}:{sourceMethod}");
        }

        public void LogSuccess(ProviderKind provider, string sourceMethod, string message, TimeSpan duration)
        {
            Events.Add($"success:{provider}:{sourceMethod}");
        }

        public void LogFailure(ProviderKind provider, string sourceMethod, string message, TimeSpan duration)
        {
            Events.Add($"failure:{provider}:{sourceMethod}");
        }

        public void LogRefreshStarted()
        {
            Events.Add("refresh-started");
        }

        public void LogRefreshCompleted(TimeSpan duration)
        {
            Events.Add("refresh-completed");
        }

        public void Clear()
        {
        }
    }
}
