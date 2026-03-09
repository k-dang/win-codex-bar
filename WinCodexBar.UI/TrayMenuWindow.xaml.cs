using System;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI;
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
    private AppWindow? _appWindow;
    private bool _allowClose;

    public TrayMenuWindow(TrayMenuViewModel viewModel, IntPtr ownerHwnd)
    {
        _viewModel = viewModel;
        _ownerHwnd = ownerHwnd;
        InitializeComponent();

        InitializeWindowChrome();
        Activated += TrayMenuWindow_Activated;
    }

    public void ShowAt(int screenX, int screenY)
    {
        RebuildMenuItems();
        PositionWindow(screenX, screenY);
        _appWindow?.Show();
        Activate();
    }

    public void HideMenu()
    {
        _appWindow?.Hide();
    }

    public void RequestClose()
    {
        _allowClose = true;
        Close();
    }

    private void InitializeWindowChrome()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
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

        SetWindowLongPtr(hwnd, GWL_HWNDPARENT, _ownerHwnd);
        var style = GetWindowLongPtr(hwnd, GWL_STYLE);
        style &= ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        style |= WsPopup;
        SetWindowLongPtr(hwnd, GWL_STYLE, style);

        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, exStyle);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        var cornerPreference = DWM_WINDOW_CORNER_PREFERENCE_DONOTROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(uint));

        var borderColor = DWM_COLOR_NONE;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(uint));
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

    private void RebuildMenuItems()
    {
        MenuItemsHost.Children.Clear();

        foreach (var item in _viewModel.Items)
        {
            switch (item.Kind)
            {
                case TrayMenuItemKind.Provider:
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
            return;
        }

        args.Cancel = true;
        sender.Hide();
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);
}
