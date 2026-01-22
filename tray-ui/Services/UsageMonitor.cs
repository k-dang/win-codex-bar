using System;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.UI.Dispatching;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class UsageMonitor
{
    private readonly SettingsStore _settingsStore;
    private readonly ProviderUsageService _providerUsageService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _timer;
    private readonly IDiagnosticsLogger? _diagnosticsLogger;
    private AppSettings _settings = AppSettings.CreateDefault();

    public UsageMonitor(
        SettingsStore settingsStore,
        ProviderUsageService providerUsageService,
        DispatcherQueue dispatcherQueue,
        IDiagnosticsLogger? diagnosticsLogger = null)
    {
        _settingsStore = settingsStore;
        _providerUsageService = providerUsageService;
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

    public async Task RefreshProviderAsync(ProviderKind provider)
    {
        var snapshot = await _providerUsageService.FetchProviderAsync(_settings, provider);
        if (snapshot == null)
        {
            return;
        }

        var summary = new UsageSummary
        {
            LastUpdated = DateTimeOffset.Now
        };

        foreach (var existing in Summary.ProviderSnapshots.Where(item => item.Provider != provider))
        {
            summary.ProviderSnapshots.Add(existing);
        }

        summary.ProviderSnapshots.Add(snapshot);
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
