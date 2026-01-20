using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class UsageMonitor
{
    private readonly SettingsStore _settingsStore;
    private readonly ProviderUsageService _providerUsageService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _timer;
    private AppSettings _settings = AppSettings.CreateDefault();

    public UsageMonitor(
        SettingsStore settingsStore,
        ProviderUsageService providerUsageService,
        DispatcherQueue dispatcherQueue)
    {
        _settingsStore = settingsStore;
        _providerUsageService = providerUsageService;
        _dispatcherQueue = dispatcherQueue;

        _timer = dispatcherQueue.CreateTimer();
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public AppSettings Settings => _settings;
    public UsageSummary Summary { get; private set; } = new();
    public event EventHandler<UsageSummary>? SummaryUpdated;

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        ConfigureTimer();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var providerSnapshots = await _providerUsageService.FetchAsync(_settings);
        var summary = new UsageSummary
        {
            LastUpdated = DateTimeOffset.Now
        };
        summary.ProviderSnapshots.AddRange(providerSnapshots);
        Summary = summary;
        _dispatcherQueue.TryEnqueue(() => SummaryUpdated?.Invoke(this, Summary));
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        _settings = settings;
        await _settingsStore.SaveAsync(settings);
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
