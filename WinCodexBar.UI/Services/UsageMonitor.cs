using System;
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
    private AppSettings _settings = AppSettings.CreateDefault();

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
    public event EventHandler<UsageSummary>? SummaryUpdated;

    public async Task InitializeAsync()
    {
        ApplySettings(await _settingsStore.LoadAsync());
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        Summary = await _usageRefreshPipeline.RefreshAsync(_settings);
        _dispatcherQueue.TryEnqueue(() => SummaryUpdated?.Invoke(this, Summary));
    }

    public async Task RefreshProviderAsync(ProviderKind provider)
    {
        Summary = await _usageRefreshPipeline.RefreshProviderAsync(_settings, Summary, provider);
        _dispatcherQueue.TryEnqueue(() => SummaryUpdated?.Invoke(this, Summary));
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
}


