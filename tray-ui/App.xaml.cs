using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using tray_ui.Models;
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
    private DiagnosticsLogger? _diagnosticsLogger;

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

        _diagnosticsLogger = new DiagnosticsLogger();
        var providerUsageService = new ProviderUsageService(logger: _diagnosticsLogger);
        _monitor = new UsageMonitor(settingsStore, providerUsageService, dispatcher, _diagnosticsLogger);

        _window = new MainWindow(_monitor);
        _window.Activate();

        if (_window is MainWindow mainWindow)
        {
            _diagnosticsLogger.EntryAdded += (_, entry) =>
            {
                dispatcher.TryEnqueue(() => mainWindow.ViewModel.AddDiagnosticsEntry(entry));
            };
        }

        var hwnd = WindowNative.GetWindowHandle(_window);
        _trayService = new TrayService(hwnd);
        _trayService.OpenRequested += (_, _) => ShowMainWindow();
        _trayService.ExitRequested += (_, _) => ExitApplication();
        _monitor.SummaryUpdated += (_, summary) => _trayService.UpdateUsageSummary(summary);

        _ = _monitor.InitializeAsync();
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
