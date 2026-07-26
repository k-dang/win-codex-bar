using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using WinCodexBar.Core.Models;
using WinCodexBar.Core.Services;

namespace WinCodexBar.UI.Services;

public sealed class UsageMonitor
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IUsageRefreshPipeline _usageRefreshPipeline;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _timer;
    private readonly IDiagnosticsLogger? _diagnosticsLogger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private AppSettings _settings = AppSettings.CreateDefault();
    private int _refreshRequestCount;

    public UsageMonitor(
        IAppSettingsStore settingsStore,
        IUsageRefreshPipeline usageRefreshPipeline,
        DispatcherQueue dispatcherQueue,
        IDiagnosticsLogger? diagnosticsLogger = null)
    {
        _settingsStore = settingsStore;
        _usageRefreshPipeline = usageRefreshPipeline;
        _dispatcherQueue = dispatcherQueue;
        _diagnosticsLogger = diagnosticsLogger;

        _timer = dispatcherQueue.CreateTimer();
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public IDiagnosticsLogger? DiagnosticsLogger => _diagnosticsLogger;

    public AppSettings Settings => _settings;
    public UsageSummary Summary { get; private set; } = new();
    public bool IsRefreshing => Volatile.Read(ref _refreshRequestCount) > 0;
    public event EventHandler<UsageSummary>? SummaryUpdated;
    public event EventHandler<bool>? RefreshStateChanged;

    public async Task InitializeAsync()
    {
        await ExecuteRefreshAsync(async () =>
        {
            ApplySettings(await _settingsStore.LoadAsync());
            return await _usageRefreshPipeline.RefreshAsync(_settings);
        });
    }

    public async Task RefreshAsync()
    {
        await ExecuteRefreshAsync(() => _usageRefreshPipeline.RefreshAsync(_settings));
    }

    public async Task RefreshProviderAsync(ProviderKind provider)
    {
        await ExecuteRefreshAsync(() => _usageRefreshPipeline.RefreshProviderAsync(_settings, Summary, provider));
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        ApplySettings(await _settingsStore.SaveAsync(settings));
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        ConfigureTimer();
    }

    private void ConfigureTimer()
    {
        _timer.Stop();
        var minutes = Math.Max(1, _settings.RefreshMinutes);
        _timer.Interval = TimeSpan.FromMinutes(minutes);
        _timer.Start();
    }

    private async Task ExecuteRefreshAsync(Func<Task<UsageSummary>> refresh)
    {
        if (Interlocked.Increment(ref _refreshRequestCount) == 1)
        {
            PublishRefreshState(isRefreshing: true);
        }

        await _refreshGate.WaitAsync();
        try
        {
            var summary = await refresh();
            Summary = summary;
            _dispatcherQueue.TryEnqueue(() => SummaryUpdated?.Invoke(this, summary));
        }
        finally
        {
            _refreshGate.Release();
            if (Interlocked.Decrement(ref _refreshRequestCount) == 0)
            {
                PublishRefreshState(isRefreshing: false);
            }
        }
    }

    private void PublishRefreshState(bool isRefreshing)
    {
        _dispatcherQueue.TryEnqueue(() => RefreshStateChanged?.Invoke(this, isRefreshing));
    }
}


