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
    private const int DefaultWindowHeight = 500;
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
    private SettingsWindow? _settingsWindow;

    public MainViewModel ViewModel { get; } = new();

    public MainWindow(UsageMonitor monitor)
    {
        _monitor = monitor;
        InitializeComponent();

        InitializeWindowEvents();
        ConfigureCustomTitleBar();
        SetInitialWindowSize(DefaultWindowWidth, DefaultWindowHeight);
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

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_monitor, this);
            _settingsWindow.Closed += SettingsWindow_Closed;
        }

        _settingsWindow.Activate();
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

    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= SettingsWindow_Closed;
            _settingsWindow = null;
        }
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
