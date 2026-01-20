using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using tray_ui.Models;

namespace tray_ui.Services;

public sealed class UsageMonitor
{
    private readonly SettingsStore _settingsStore;
    private readonly LogScanner _scanner;
    private readonly ProviderUsageService _providerUsageService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _debounceTimer;
    private readonly List<FileSystemWatcher> _watchers = new();
    private AppSettings _settings = AppSettings.CreateDefault();

    public UsageMonitor(
        SettingsStore settingsStore,
        LogScanner scanner,
        ProviderUsageService providerUsageService,
        DispatcherQueue dispatcherQueue)
    {
        _settingsStore = settingsStore;
        _scanner = scanner;
        _providerUsageService = providerUsageService;
        _dispatcherQueue = dispatcherQueue;

        _timer = dispatcherQueue.CreateTimer();
        _timer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(false);

        _debounceTimer = dispatcherQueue.CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromSeconds(2);
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += async (_, _) => await RefreshAsync().ConfigureAwait(false);
    }

    public AppSettings Settings => _settings;
    public UsageSummary Summary { get; private set; } = new();
    public event EventHandler<UsageSummary>? SummaryUpdated;

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync().ConfigureAwait(false);
        EnsureLogRoots(_settings);
        ConfigureTimer();
        ConfigureWatchers();
        await RefreshAsync().ConfigureAwait(false);
    }

    public async Task RefreshAsync()
    {
        var result = await _scanner.ScanAsync(_settings).ConfigureAwait(false);
        var providerSnapshots = await _providerUsageService.FetchAsync(_settings).ConfigureAwait(false);
        result.Summary.ProviderSnapshots.Clear();
        result.Summary.ProviderSnapshots.AddRange(providerSnapshots);
        result.Summary.LogRoots.Clear();
        result.Summary.LogRoots.AddRange(_settings.LogRoots);
        result.Summary.ScanErrors.Clear();
        result.Summary.ScanErrors.AddRange(result.Errors);
        Summary = NormalizeSummary(result.Summary);
        _dispatcherQueue.TryEnqueue(() => SummaryUpdated?.Invoke(this, Summary));
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        _settings = settings;
        EnsureLogRoots(_settings);
        await _settingsStore.SaveAsync(settings).ConfigureAwait(false);
        ConfigureTimer();
        ConfigureWatchers();
    }

    private static void EnsureLogRoots(AppSettings settings)
    {
        if (settings.LogRoots == null || settings.LogRoots.Count == 0)
        {
            settings.LogRoots = AppSettings.CreateDefault().LogRoots;
        }
        else
        {
            SettingsStore.AddCodexSessionsRoot(settings.LogRoots);
        }
    }

    private void ConfigureTimer()
    {
        _timer.Stop();
        var minutes = Math.Max(1, _settings.RefreshMinutes);
        _timer.Interval = TimeSpan.FromMinutes(minutes);
        _timer.Start();
    }

    private void ConfigureWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();

        if (!_settings.WatchFileChanges)
        {
            return;
        }

        foreach (var root in _settings.LogRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(root, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            watcher.Changed += OnWatcherEvent;
            watcher.Created += OnWatcherEvent;
            watcher.Renamed += OnWatcherEvent;
            watcher.EnableRaisingEvents = true;
            _watchers.Add(watcher);
        }
    }

    private void OnWatcherEvent(object sender, FileSystemEventArgs e)
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private static UsageSummary NormalizeSummary(UsageSummary summary)
    {
        var today = DateTime.Today;
        var start = today.AddDays(-29);
        var items = summary.DailyTotals
            .Where(item => item.Date >= start)
            .OrderBy(item => item.Date)
            .ToList();

        summary.DailyTotals.Clear();
        summary.DailyTotals.AddRange(items);
        return summary;
    }
}
