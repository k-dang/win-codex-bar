using System;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using tray_ui.Models;
using tray_ui.Services;
using tray_ui.ViewModels;
using Windows.UI;
using WinRT;
using WinRT.Interop;

namespace tray_ui;

public sealed partial class MainWindow : Window
{
    private const int DefaultWindowWidth = 420;
    private const int MinWindowHeight = 420;
    private const int MaxWindowHeight = 720;
    private const int WindowChromeHeight = 8;
    private readonly UsageMonitor _monitor;
    private AppWindow? _appWindow;
    private AppWindowTitleBar? _titleBar;
    private bool _allowClose;
    private bool _resizePending;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfiguration;

    public MainViewModel ViewModel { get; } = new();

    public MainWindow(UsageMonitor monitor)
    {
        _monitor = monitor;
        InitializeComponent();

        InitializeWindowEvents();
        ConfigureCustomTitleBar();
        SetInitialWindowSize(DefaultWindowWidth, 600);
        RootGrid.Loaded += MainWindow_Loaded;
        RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;
        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
        SizeChanged += MainWindow_SizeChanged;

        RootGrid.DataContext = ViewModel;
        ViewModel.Update(_monitor.Summary);
        _monitor.SummaryUpdated += OnSummaryUpdated;

        TrySetThinAcrylicBackdrop();
        UpdateTitleBarStyle(RootGrid.ActualTheme);
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

    private void OnSummaryUpdated(object? sender, UsageSummary summary)
    {
        ViewModel.Update(summary);
        ScheduleResizeToContentHeight();
    }

    private async void RetryProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is ProviderKind provider)
        {
            await _monitor.RefreshProviderAsync(provider);
        }
    }

    private void ProviderSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        CodexPanel.Visibility = sender.SelectedItem == SelectorCodex ? Visibility.Visible : Visibility.Collapsed;
        ClaudePanel.Visibility = sender.SelectedItem == SelectorClaude ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPanel.Visibility = sender.SelectedItem == SelectorDiagnostics ? Visibility.Visible : Visibility.Collapsed;
        ScheduleResizeToContentHeight();
    }

    private void DiagnosticsFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var filter = DiagnosticsFilterComboBox.SelectedIndex switch
        {
            1 => "Codex",
            2 => "Claude",
            _ => "All"
        };
        ViewModel.SelectedProviderFilter = filter;
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

        var claudeSettings = new Models.ProviderSettings
        {
            Enabled = SettingsClaudeEnabledBox.IsChecked == true,
            SourceMode = ParseSourceMode(SettingsClaudeSourceBox.SelectedIndex),
            CookieSource = ParseCookieSource(SettingsClaudeCookieSourceBox.SelectedIndex),
            CookieHeader = SettingsClaudeCookieHeaderBox.Text?.Trim()
        };

        var settings = new Models.AppSettings
        {
            RefreshMinutes = (int)Math.Max(1, refreshValue),
            Codex = codexSettings,
            Claude = claudeSettings
        };

        await _monitor.SaveSettingsAsync(settings);
    }

    private void LoadSettings()
    {
        SettingsRefreshMinutesBox.Value = _monitor.Settings.RefreshMinutes;

        SettingsCodexEnabledBox.IsChecked = _monitor.Settings.Codex.Enabled;
        SettingsCodexSourceBox.SelectedIndex = SourceIndex(_monitor.Settings.Codex.SourceMode);
        SettingsCodexCookieSourceBox.SelectedIndex = CookieIndex(_monitor.Settings.Codex.CookieSource);
        SettingsCodexCookieHeaderBox.Text = _monitor.Settings.Codex.CookieHeader ?? string.Empty;

        SettingsClaudeEnabledBox.IsChecked = _monitor.Settings.Claude.Enabled;
        SettingsClaudeSourceBox.SelectedIndex = SourceIndex(_monitor.Settings.Claude.SourceMode);
        SettingsClaudeCookieSourceBox.SelectedIndex = CookieIndex(_monitor.Settings.Claude.CookieSource);
        SettingsClaudeCookieHeaderBox.Text = _monitor.Settings.Claude.CookieHeader ?? string.Empty;
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

    private void ConfigureCustomTitleBar()
    {
        if (_appWindow == null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            ExtendsContentIntoTitleBar = false;
            return;
        }

        ExtendsContentIntoTitleBar = true;
        _titleBar = _appWindow.TitleBar;
        _titleBar.ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarDragRegion);
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        if (_titleBar == null)
        {
            return;
        }

        TitleBarRightInset.Width = _titleBar.RightInset;
    }

    private void UpdateTitleBarStyle(ElementTheme theme)
    {
        if (_appWindow == null)
        {
            return;
        }

        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = _appWindow.TitleBar;
        var isDark = theme == ElementTheme.Dark || theme == ElementTheme.Default;
        var foreground = isDark ? Colors.White : Colors.Black;
        var inactiveForeground = isDark ? Color.FromArgb(180, 255, 255, 255) : Color.FromArgb(160, 0, 0, 0);
        var hoverBackground = isDark ? Color.FromArgb(36, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
        var pressedBackground = isDark ? Color.FromArgb(56, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);

        titleBar.BackgroundColor = Colors.Transparent;
        titleBar.InactiveBackgroundColor = Colors.Transparent;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = hoverBackground;
        titleBar.ButtonPressedBackgroundColor = pressedBackground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
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

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ScheduleResizeToContentHeight();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropConfiguration != null)
        {
            _backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }
    }

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        UpdateTitleBarInsets();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_acrylicController != null)
        {
            _acrylicController.Dispose();
            _acrylicController = null;
        }

        _titleBar = null;

        _backdropConfiguration = null;
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_backdropConfiguration != null)
        {
            _backdropConfiguration.Theme = MapTheme(sender.ActualTheme);
        }

        UpdateTitleBarStyle(sender.ActualTheme);
    }

    private bool TrySetThinAcrylicBackdrop()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return false;
        }

        _backdropConfiguration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = MapTheme(RootGrid.ActualTheme)
        };

        _acrylicController = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin
        };

        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
        return true;
    }

    private static SystemBackdropTheme MapTheme(ElementTheme theme)
    {
        return theme switch
        {
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _ => SystemBackdropTheme.Default
        };
    }

    private void ScheduleResizeToContentHeight()
    {
        if (_resizePending)
        {
            return;
        }

        _resizePending = true;
        _ = DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _resizePending = false;
            ResizeToContentHeight();
        });
    }

    private void ResizeToContentHeight()
    {
        if (_appWindow == null)
        {
            return;
        }

        var targetWidth = _appWindow.Size.Width;
        if (targetWidth <= 0)
        {
            targetWidth = DefaultWindowWidth;
        }

        RootGrid.Measure(new Windows.Foundation.Size(targetWidth, double.PositiveInfinity));
        var desiredHeight = RootGrid.DesiredSize.Height + WindowChromeHeight;
        var clampedHeight = Math.Clamp((int)Math.Round(desiredHeight), MinWindowHeight, MaxWindowHeight);

        if (clampedHeight != _appWindow.Size.Height)
        {
            _appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = targetWidth, Height = clampedHeight });
        }
    }
}
