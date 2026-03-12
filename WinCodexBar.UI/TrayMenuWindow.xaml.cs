using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinCodexBar.UI.ViewModels;
using WinRT.Interop;

namespace WinCodexBar.UI;

public sealed partial class TrayMenuWindow : Window
{
    private const int MenuWidth = 280;
    private const int MenuOffset = 4;
    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const int GWL_HWNDPARENT = -8;
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const uint DWM_WINDOW_CORNER_PREFERENCE_DONOTROUND = 1;
    private const uint DWM_COLOR_NONE = 0xFFFFFFFE;
    private const nint WS_CAPTION = 0x00C00000;
    private const nint WS_THICKFRAME = 0x00040000;
    private const nint WS_SYSMENU = 0x00080000;
    private const nint WS_MINIMIZEBOX = 0x00020000;
    private const nint WS_MAXIMIZEBOX = 0x00010000;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const nint WS_EX_APPWINDOW = 0x00040000;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private static readonly nint WsPopup = unchecked((nint)0x80000000u);
    private readonly TrayMenuViewModel _viewModel;
    private readonly IntPtr _ownerHwnd;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly LowLevelMouseProc _mouseHookProc;
    private AppWindow? _appWindow;
    private IntPtr _hwnd;
    private IntPtr _mouseHookHandle;
    private bool _allowClose;

    public bool IsMenuVisible { get; private set; }

    public TrayMenuWindow(TrayMenuViewModel viewModel, IntPtr ownerHwnd)
    {
        _viewModel = viewModel;
        _ownerHwnd = ownerHwnd;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("Tray menu window requires a dispatcher queue.");
        _mouseHookProc = MouseHookCallback;
        InitializeComponent();

        InitializeWindowChrome();
        Activated += TrayMenuWindow_Activated;
    }

    public void ShowAt(int screenX, int screenY)
    {
        RebuildMenuItems();
        PositionWindow(screenX, screenY);
        _appWindow?.Show();
        IsMenuVisible = true;
        EnsureOutsideClickHook();
        Activate();
    }

    public void HideMenu()
    {
        RemoveOutsideClickHook();
        IsMenuVisible = false;
        _appWindow?.Hide();
    }

    public void RequestClose()
    {
        _allowClose = true;
        RemoveOutsideClickHook();
        IsMenuVisible = false;
        Close();
    }

    private void InitializeWindowChrome()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += AppWindow_Closing;
        if (_appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        SetWindowLongPtr(_hwnd, GWL_HWNDPARENT, _ownerHwnd);
        var style = GetWindowLongPtr(_hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        style |= WsPopup;
        SetWindowLongPtr(_hwnd, GWL_STYLE, style);

        var exStyle = GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        exStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, exStyle);
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        var cornerPreference = DWM_WINDOW_CORNER_PREFERENCE_DONOTROUND;
        _ = DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));

        var borderColor = DWM_COLOR_NONE;
        _ = DwmSetWindowAttribute(_hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
    }

    private void TrayMenuWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            HideMenu();
        }
    }

    private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            HideMenu();
            e.Handled = true;
        }
    }

    private void RootGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        HideMenu();
        e.Handled = true;
    }

    private void RebuildMenuItems()
    {
        MenuItemsHost.Children.Clear();

        foreach (var item in _viewModel.Items)
        {
            switch (item.Kind)
            {
                case TrayMenuItemKind.Provider:
                    MenuItemsHost.Children.Add(CreateProviderBlock(item));
                    break;
                case TrayMenuItemKind.Empty:
                    MenuItemsHost.Children.Add(new TextBlock
                    {
                        Text = item.Text,
                        Style = (Style)Application.Current.Resources["TrayMenuInfoTextStyle"],
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(12, 7, 12, 7),
                        Opacity = item.IsEnabled ? 1.0 : 0.72
                    });
                    break;
                case TrayMenuItemKind.Open:
                case TrayMenuItemKind.Exit:
                    var button = new Button
                    {
                        Content = item.Text,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Style = (Style)Application.Current.Resources["TrayMenuActionButtonStyle"],
                        Tag = item
                    };
                    button.Click += ActionButton_Click;
                    MenuItemsHost.Children.Add(button);
                    break;
                case TrayMenuItemKind.Separator:
                    MenuItemsHost.Children.Add(new Border
                    {
                        Height = 1,
                        Margin = new Thickness(8, 4, 8, 4),
                        Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TrayMenuSeparatorBrush"]
                    });
                    break;
            }
        }
    }

    private static FrameworkElement CreateProviderBlock(TrayMenuItem item)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(12, 9, 12, 9)
        };

        panel.Children.Add(new TextBlock
        {
            Text = item.Text,
            Style = (Style)Application.Current.Resources["TrayMenuProviderTitleTextStyle"]
        });

        if (!string.IsNullOrWhiteSpace(item.ErrorText))
        {
            panel.Children.Add(new TextBlock
            {
                Text = item.ErrorText,
                Style = (Style)Application.Current.Resources["TrayMenuInfoTextStyle"],
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.82
            });

            return panel;
        }

        if (item.PrimaryMetric != null)
        {
            panel.Children.Add(CreateMetricRow(item.PrimaryMetric));
        }

        if (item.SecondaryMetric != null)
        {
            panel.Children.Add(CreateMetricRow(item.SecondaryMetric));
        }

        return panel;
    }

    private static Grid CreateMetricRow(TrayMenuMetric metric)
    {
        var grid = new Grid
        {
            ColumnSpacing = 8,
            Opacity = metric.PercentValue.HasValue ? 1.0 : 0.62
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = metric.Label,
            Style = (Style)Application.Current.Resources["TrayMenuMetricLabelTextStyle"],
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 42
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var progressBar = new ProgressBar
        {
            Style = (Style)Application.Current.Resources["TrayMenuProgressBarStyle"],
            Value = ClampPercent(metric.PercentValue),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(progressBar, 1);
        grid.Children.Add(progressBar);

        var percent = new TextBlock
        {
            Text = metric.PercentText,
            Style = (Style)Application.Current.Resources["TrayMenuMetricPercentTextStyle"],
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(percent, 2);
        grid.Children.Add(percent);

        return grid;
    }

    private static double ClampPercent(double? value)
    {
        if (!value.HasValue)
        {
            return 0;
        }

        return Math.Clamp(value.Value, 0, 100);
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrayMenuItem item })
        {
            _viewModel.ActivateItem(item);
            HideMenu();
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            RemoveOutsideClickHook();
            return;
        }

        args.Cancel = true;
        sender.Hide();
    }

    private void EnsureOutsideClickHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        var moduleHandle = currentModule != null ? GetModuleHandle(currentModule.ModuleName) : IntPtr.Zero;
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, moduleHandle, 0);
    }

    private void RemoveOutsideClickHook()
    {
        if (_mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsMenuVisible && lParam != IntPtr.Zero && IsOutsideDismissMessage(wParam))
        {
            var hookData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (!IsPointInsideMenu(hookData.Point))
            {
                _dispatcherQueue.TryEnqueue(HideMenu);
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private bool IsPointInsideMenu(POINT point)
    {
        if (_hwnd == IntPtr.Zero || !GetWindowRect(_hwnd, out var rect))
        {
            return false;
        }

        return point.X >= rect.Left &&
               point.X < rect.Right &&
               point.Y >= rect.Top &&
               point.Y < rect.Bottom;
    }

    private static bool IsOutsideDismissMessage(IntPtr wParam)
    {
        var message = wParam.ToInt32();
        return message == WM_LBUTTONDOWN ||
               message == WM_MBUTTONDOWN ||
               message == WM_XBUTTONDOWN;
    }

    private void PositionWindow(int screenX, int screenY)
    {
        if (_appWindow == null)
        {
            return;
        }

        RootGrid.Measure(new Windows.Foundation.Size(MenuWidth, double.PositiveInfinity));
        var desiredHeight = (int)Math.Ceiling(RootGrid.DesiredSize.Height);
        var menuHeight = Math.Max(desiredHeight, 1);
        _appWindow.Resize(new Windows.Graphics.SizeInt32(MenuWidth, menuHeight));

        var displayArea = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(screenX, screenY), DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;

        var x = screenX;
        var y = screenY - menuHeight - MenuOffset;

        if (y < workArea.Y)
        {
            y = screenY + MenuOffset;
        }

        if (x + MenuWidth > workArea.X + workArea.Width)
        {
            x = workArea.X + workArea.Width - MenuWidth;
        }

        if (y + menuHeight > workArea.Y + workArea.Height)
        {
            y = workArea.Y + workArea.Height - menuHeight;
        }

        x = Math.Max(x, workArea.X);
        y = Math.Max(y, workArea.Y);

        _appWindow.Move(new Windows.Graphics.PointInt32(x, y));
        var firstButton = MenuItemsHost.Children.OfType<Button>().FirstOrDefault();
        if (firstButton != null)
        {
            firstButton.Focus(FocusState.Programmatic);
        }
        else
        {
            RootGrid.Focus(FocusState.Programmatic);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hmod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Point;
        public int MouseData;
        public int Flags;
        public int Time;
        public IntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
