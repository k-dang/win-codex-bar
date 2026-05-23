using WinCodexBar.Core.Models;
using WinCodexBar.Core.Services;

namespace WinCodexBar.Tests;

public class UsageRefreshPipelineTests
{
    [Fact]
    public async Task RefreshAsync_ReturnsEnabledProvidersInProviderOrder()
    {
        var settings = AppSettings.CreateDefault();
        var pipeline = new UsageRefreshPipeline(
            [
                new FakeSource(ProviderKind.Claude, ProviderSourceMode.OAuth, _ => Snapshot(ProviderKind.Claude, "oauth")),
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.OAuth, _ => Snapshot(ProviderKind.Codex, "oauth"))
            ],
            clock: new FakeClock());

        var summary = await pipeline.RefreshAsync(settings);

        Assert.Collection(
            summary.ProviderSnapshots,
            snapshot => Assert.Equal(ProviderKind.Codex, snapshot.Provider),
            snapshot => Assert.Equal(ProviderKind.Claude, snapshot.Provider));
    }

    [Fact]
    public async Task RefreshAsync_SkipsDisabledProviders()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;

        var codexCalls = 0;
        var claudeCalls = 0;
        var pipeline = new UsageRefreshPipeline(
            [
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.OAuth, _ =>
                {
                    codexCalls++;
                    return Snapshot(ProviderKind.Codex, "oauth");
                }),
                new FakeSource(ProviderKind.Claude, ProviderSourceMode.OAuth, _ =>
                {
                    claudeCalls++;
                    return Snapshot(ProviderKind.Claude, "oauth");
                })
            ],
            clock: new FakeClock());

        var summary = await pipeline.RefreshAsync(settings);

        var snapshot = Assert.Single(summary.ProviderSnapshots);
        Assert.Equal(ProviderKind.Codex, snapshot.Provider);
        Assert.Equal(1, codexCalls);
        Assert.Equal(0, claudeCalls);
    }

    [Fact]
    public async Task RefreshAsync_WhenSourceReturnsNull_TriesNextSource()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;
        var attempts = new List<ProviderSourceMode>();
        var pipeline = new UsageRefreshPipeline(
            [
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.OAuth, _ =>
                {
                    attempts.Add(ProviderSourceMode.OAuth);
                    return null;
                }),
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.Web, _ =>
                {
                    attempts.Add(ProviderSourceMode.Web);
                    return Snapshot(ProviderKind.Codex, "web");
                }),
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.Cli, _ =>
                {
                    attempts.Add(ProviderSourceMode.Cli);
                    return Snapshot(ProviderKind.Codex, "cli");
                })
            ],
            clock: new FakeClock());

        var summary = await pipeline.RefreshAsync(settings);

        var snapshot = Assert.Single(summary.ProviderSnapshots);
        Assert.Equal("web", snapshot.SourceLabel);
        Assert.Equal([ProviderSourceMode.OAuth, ProviderSourceMode.Web], attempts);
    }

    [Fact]
    public async Task RefreshAsync_WhenAllSourcesFail_ReturnsUserFacingErrorSnapshot()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;
        var pipeline = new UsageRefreshPipeline(
            [
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.OAuth, _ => throw new InvalidOperationException("OAuth failed")),
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.Web, _ => throw new InvalidOperationException("Web failed")),
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.Cli, _ => null)
            ],
            clock: new FakeClock());

        var summary = await pipeline.RefreshAsync(settings);

        var snapshot = Assert.Single(summary.ProviderSnapshots);
        Assert.Equal(ProviderKind.Codex, snapshot.Provider);
        Assert.Equal("auto", snapshot.SourceLabel);
        Assert.Equal("OAuth failed Web failed", snapshot.Error);
    }

    [Fact]
    public async Task RefreshProviderAsync_ReplacesOnlyTargetProvider()
    {
        var settings = AppSettings.CreateDefault();
        var existing = new UsageSummary { LastUpdated = DateTimeOffset.UnixEpoch };
        existing.ProviderSnapshots.Add(Snapshot(ProviderKind.Codex, "old"));
        existing.ProviderSnapshots.Add(Snapshot(ProviderKind.Claude, "old"));
        var now = new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero);
        var pipeline = new UsageRefreshPipeline(
            [
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.OAuth, _ => Snapshot(ProviderKind.Codex, "new"))
            ],
            clock: new FakeClock(now));

        var summary = await pipeline.RefreshProviderAsync(settings, existing, ProviderKind.Codex);

        Assert.Equal(now, summary.LastUpdated);
        Assert.Collection(
            summary.ProviderSnapshots.OrderBy(snapshot => snapshot.Provider),
            snapshot =>
            {
                Assert.Equal(ProviderKind.Codex, snapshot.Provider);
                Assert.Equal("new", snapshot.SourceLabel);
            },
            snapshot =>
            {
                Assert.Equal(ProviderKind.Claude, snapshot.Provider);
                Assert.Equal("old", snapshot.SourceLabel);
            });
    }

    [Fact]
    public async Task RefreshProviderAsync_WhenProviderDisabled_PreservesCurrentSummary()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Codex).Enabled = false;
        var existing = new UsageSummary();
        existing.ProviderSnapshots.Add(Snapshot(ProviderKind.Codex, "old"));
        var pipeline = new UsageRefreshPipeline(
            [new FakeSource(ProviderKind.Codex, ProviderSourceMode.OAuth, _ => Snapshot(ProviderKind.Codex, "new"))],
            clock: new FakeClock());

        var summary = await pipeline.RefreshProviderAsync(settings, existing, ProviderKind.Codex);

        Assert.Same(existing, summary);
        Assert.Equal("old", Assert.Single(summary.ProviderSnapshots).SourceLabel);
    }

    [Fact]
    public async Task RefreshAsync_EmitsRefreshAndSourceDiagnosticsInOrder()
    {
        var settings = AppSettings.CreateDefault();
        settings.GetProviderSettings(ProviderKind.Claude).Enabled = false;
        var logger = new RecordingLogger();
        var pipeline = new UsageRefreshPipeline(
            [
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.OAuth, _ => null),
                new FakeSource(ProviderKind.Codex, ProviderSourceMode.Web, _ => Snapshot(ProviderKind.Codex, "web"))
            ],
            logger,
            new FakeClock());

        await pipeline.RefreshAsync(settings);

        Assert.Equal(
            [
                "refresh-started",
                "attempt:Codex:OAuth",
                "failure:Codex:OAuth",
                "attempt:Codex:Web",
                "success:Codex:Web",
                "refresh-completed"
            ],
            logger.Events);
    }

    private static ProviderUsageSnapshot Snapshot(ProviderKind provider, string sourceLabel)
    {
        return new ProviderUsageSnapshot
        {
            Provider = provider,
            SourceLabel = sourceLabel,
            Primary = new UsageWindow { Label = "Session", UsedPercent = 42 },
            UpdatedAt = DateTimeOffset.UnixEpoch
        };
    }

    private sealed class FakeSource : IProviderUsageSource
    {
        private readonly Func<ProviderUsageSourceRequest, ProviderUsageSnapshot?> _fetch;

        public FakeSource(
            ProviderKind provider,
            ProviderSourceMode sourceMode,
            Func<ProviderUsageSourceRequest, ProviderUsageSnapshot?> fetch)
        {
            Provider = provider;
            SourceMode = sourceMode;
            _fetch = fetch;
        }

        public ProviderKind Provider { get; }
        public ProviderSourceMode SourceMode { get; }

        public Task<ProviderUsageSnapshot?> FetchAsync(
            ProviderUsageSourceRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_fetch(request));
        }
    }

    private sealed class FakeClock : IUsageClock
    {
        public FakeClock()
            : this(new DateTimeOffset(2026, 5, 22, 12, 0, 0, TimeSpan.Zero))
        {
        }

        public FakeClock(DateTimeOffset now)
        {
            Now = now;
        }

        public DateTimeOffset Now { get; }

        public IUsageStopwatch StartStopwatch()
        {
            return new FakeStopwatch();
        }
    }

    private sealed class FakeStopwatch : IUsageStopwatch
    {
        public TimeSpan Elapsed => TimeSpan.FromMilliseconds(1);

        public void Stop()
        {
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
