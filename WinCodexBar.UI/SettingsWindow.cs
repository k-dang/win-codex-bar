using System;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinCodexBar.UI.Services;
using Windows.UI;
using WinRT;
using WinRT.Interop;

namespace WinCodexBar.UI;

public sealed class SettingsWindow : Window
{
    private readonly SettingsPage _page;
    private AppWindow? _appWindow;
    private AppWindowTitleBar? _titleBar;
    private DesktopAcrylicController? _acrylicController;
    private SystemBackdropConfiguration? _backdropConfiguration;

    public SettingsWindow(UsageMonitor monitor, Window owner)
    {
        Title = "Settings";
        SetInitialWindowSize(520, 620);

        _page = new SettingsPage(monitor);
        _page.CloseRequested += Page_CloseRequested;
        Content = _page;

        InitializeWindowEvents();
        ConfigureCustomTitleBar();
        CenterOverOwner(owner);
        _page.RootElement.ActualThemeChanged += RootElement_ActualThemeChanged;
        Activated += SettingsWindow_Activated;
        Closed += SettingsWindow_Closed;
        SizeChanged += SettingsWindow_SizeChanged;

        TrySetThinAcrylicBackdrop();
        UpdateTitleBarStyle(_page.RootElement.ActualTheme);
    }

    private void Page_CloseRequested(object? sender, EventArgs e)
    {
        _page.CloseRequested -= Page_CloseRequested;
        Close();
    }

    private void SettingsWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_backdropConfiguration != null)
        {
            _backdropConfiguration.IsInputActive = args.WindowActivationState != WindowActivationState.Deactivated;
        }
    }

    private void SettingsWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        UpdateTitleBarInsets();
    }

    private void SettingsWindow_Closed(object sender, WindowEventArgs args)
    {
        _page.RootElement.ActualThemeChanged -= RootElement_ActualThemeChanged;

        if (_acrylicController != null)
        {
            _acrylicController.Dispose();
            _acrylicController = null;
        }

        _titleBar = null;
        _backdropConfiguration = null;
    }

    private void RootElement_ActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_backdropConfiguration != null)
        {
            _backdropConfiguration.Theme = MapTheme(sender.ActualTheme);
        }

        UpdateTitleBarStyle(sender.ActualTheme);
    }

    private void InitializeWindowEvents()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
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
        SetTitleBar(_page.TitleBarDragRegionElement);
        UpdateTitleBarInsets();
    }

    private void UpdateTitleBarInsets()
    {
        if (_titleBar == null)
        {
            return;
        }

        _page.TitleBarRightInsetElement.Width = _titleBar.RightInset;
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

    private void SetInitialWindowSize(int width, int height)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow?.Resize(new Windows.Graphics.SizeInt32 { Width = width, Height = height });
        }
        catch
        {
            // ignored
        }
    }

    private void CenterOverOwner(Window owner)
    {
        if (_appWindow == null)
        {
            return;
        }

        var ownerHwnd = WindowNative.GetWindowHandle(owner);
        var ownerId = Win32Interop.GetWindowIdFromWindow(ownerHwnd);
        var ownerAppWindow = AppWindow.GetFromWindowId(ownerId);
        if (ownerAppWindow == null)
        {
            return;
        }

        var ownerPos = ownerAppWindow.Position;
        var ownerSize = ownerAppWindow.Size;
        var targetSize = _appWindow.Size;
        var targetWidth = targetSize.Width > 0 ? targetSize.Width : 520;
        var targetHeight = targetSize.Height > 0 ? targetSize.Height : 620;
        var x = ownerPos.X + (ownerSize.Width - targetWidth) / 2;
        var y = ownerPos.Y + (ownerSize.Height - targetHeight) / 2;

        var displayArea = DisplayArea.GetFromWindowId(ownerId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;
        x = Math.Clamp(x, workArea.X, workArea.X + workArea.Width - targetWidth);
        y = Math.Clamp(y, workArea.Y, workArea.Y + workArea.Height - targetHeight);

        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    private void TrySetThinAcrylicBackdrop()
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            return;
        }

        _backdropConfiguration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = MapTheme(_page.RootElement.ActualTheme)
        };

        _acrylicController = new DesktopAcrylicController
        {
            Kind = DesktopAcrylicKind.Thin
        };

        _acrylicController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
        _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
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
}

