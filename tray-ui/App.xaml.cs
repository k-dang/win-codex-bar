using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using tray_ui.Services;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace tray_ui;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private TrayService? _trayService;
    private UsageMonitor? _monitor;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var settingsStore = new SettingsStore();
        var cacheStore = new CacheStore();
        var scanner = new LogScanner(cacheStore);
        var providerUsageService = new ProviderUsageService();
        _monitor = new UsageMonitor(settingsStore, scanner, cacheStore, providerUsageService, dispatcher);

        _window = new MainWindow(_monitor);
        _window.Activate();

        _monitor.SummaryUpdated += (_, summary) =>
        {
            var todayTotal = summary.DailyTotals.Find(item => item.Date == DateTime.Today)?.TotalTokens ?? 0;
            _trayService?.UpdateTooltip($"Today: {todayTotal} tokens");
        };

        _ = _monitor.InitializeAsync();

        var hwnd = WindowNative.GetWindowHandle(_window);
        _trayService = new TrayService(hwnd);
        _trayService.OpenRequested += (_, _) => ShowMainWindow();
        _trayService.FullRescanRequested += async (_, _) => await _monitor.FullRescanAsync().ConfigureAwait(false);
        _trayService.ExitRequested += (_, _) => ExitApplication();
    }

    private void ShowMainWindow()
    {
        if (_window is MainWindow mainWindow)
        {
            mainWindow.ShowWindow();
        }
    }

    private void ExitApplication()
    {
        _trayService?.Dispose();
        _trayService = null;
        if (_window is MainWindow mainWindow)
        {
            mainWindow.RequestClose();
        }
        else
        {
            _window?.Close();
        }
        Exit();
    }
}
