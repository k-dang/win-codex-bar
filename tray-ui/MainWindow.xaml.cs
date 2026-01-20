using System;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using tray_ui.Services;
using tray_ui.ViewModels;
using WinRT.Interop;

namespace tray_ui;

public sealed partial class MainWindow : Window
{
    private readonly UsageMonitor _monitor;
    private AppWindow? _appWindow;
    private readonly ObservableCollection<string> _settingsRoots = new();
    private bool _allowClose;

    public MainViewModel ViewModel { get; } = new();

    public MainWindow(UsageMonitor monitor)
    {
        _monitor = monitor;
        InitializeComponent();

        ExtendsContentIntoTitleBar = false;
        SetInitialWindowSize(900, 600);
        InitializeWindowEvents();

        RootGrid.DataContext = ViewModel;
        ViewModel.Update(_monitor.Summary);
        _monitor.SummaryUpdated += (_, summary) => ViewModel.Update(summary);

        SettingsRootsList.ItemsSource = _settingsRoots;
    }

    public void ShowWindow()
    {
        if (_appWindow != null)
        {
            _appWindow.Show();
            _appWindow.MoveInZOrderAtTop();
        }

        Activate();
    }

    public void RequestClose()
    {
        _allowClose = true;
        Close();
    }

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        SettingsDialog.XamlRoot = RootGrid.XamlRoot;
        await SettingsDialog.ShowAsync();
    }

    private void SettingsAddRoot_Click(object sender, RoutedEventArgs e)
    {
        var text = SettingsRootInput.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!_settingsRoots.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            _settingsRoots.Add(text);
        }

        SettingsRootInput.Text = string.Empty;
    }

    private void SettingsRemoveRoot_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsRootsList.SelectedItem is string value)
        {
            _settingsRoots.Remove(value);
        }
    }

    private async void SettingsDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var refreshValue = SettingsRefreshMinutesBox.Value;
        if (double.IsNaN(refreshValue) || refreshValue <= 0)
        {
            refreshValue = 5;
        }

        var codexSettings = new Models.ProviderSettings
        {
            Enabled = SettingsCodexEnabledBox.IsChecked == true,
            SourceMode = ParseSourceMode(SettingsCodexSourceBox.SelectedIndex),
            CookieSource = ParseCookieSource(SettingsCodexCookieSourceBox.SelectedIndex),
            CookieHeader = SettingsCodexCookieHeaderBox.Text?.Trim()
        };

        var settings = new Models.AppSettings
        {
            LogRoots = _settingsRoots.ToList(),
            RefreshMinutes = (int)Math.Max(1, refreshValue),
            WatchFileChanges = SettingsWatchChangesBox.IsChecked == true,
            Codex = codexSettings
        };

        await _monitor.SaveSettingsAsync(settings);
    }

    private void LoadSettings()
    {
        _settingsRoots.Clear();
        foreach (var root in _monitor.Settings.LogRoots)
        {
            _settingsRoots.Add(root);
        }

        SettingsRefreshMinutesBox.Value = _monitor.Settings.RefreshMinutes;
        SettingsWatchChangesBox.IsChecked = _monitor.Settings.WatchFileChanges;
        SettingsCodexEnabledBox.IsChecked = _monitor.Settings.Codex.Enabled;
        SettingsCodexSourceBox.SelectedIndex = SourceIndex(_monitor.Settings.Codex.SourceMode);
        SettingsCodexCookieSourceBox.SelectedIndex = CookieIndex(_monitor.Settings.Codex.CookieSource);
        SettingsCodexCookieHeaderBox.Text = _monitor.Settings.Codex.CookieHeader ?? string.Empty;
    }

    private static Models.ProviderSourceMode ParseSourceMode(int selectedIndex)
    {
        return selectedIndex switch
        {
            1 => Models.ProviderSourceMode.OAuth,
            2 => Models.ProviderSourceMode.Web,
            3 => Models.ProviderSourceMode.Cli,
            _ => Models.ProviderSourceMode.Auto
        };
    }

    private static int SourceIndex(Models.ProviderSourceMode mode)
    {
        return mode switch
        {
            Models.ProviderSourceMode.OAuth => 1,
            Models.ProviderSourceMode.Web => 2,
            Models.ProviderSourceMode.Cli => 3,
            _ => 0
        };
    }

    private static Models.CookieSourceMode ParseCookieSource(int selectedIndex)
    {
        return selectedIndex == 1 ? Models.CookieSourceMode.Manual : Models.CookieSourceMode.Auto;
    }

    private static int CookieIndex(Models.CookieSourceMode mode)
    {
        return mode == Models.CookieSourceMode.Manual ? 1 : 0;
    }


    private void InitializeWindowEvents()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        if (_appWindow != null)
        {
            _appWindow.Closing += AppWindow_Closing;
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    private void SetInitialWindowSize(int width, int height)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
            }
        }
        catch
        {
        }
    }
}
