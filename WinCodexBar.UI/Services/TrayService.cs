using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using WinCodexBar.Core.Models;
using Windows.ApplicationModel;

namespace WinCodexBar.UI.Services;

public sealed class TrayService : IDisposable
{
    private const int WM_APP = 0x8000;
    private const int WM_TRAYICON = WM_APP + 1;
    private const int WM_COMMAND = 0x0111;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int GWL_WNDPROC = -4;

    private const int NIM_ADD = 0x00000000;
    private const int NIM_MODIFY = 0x00000001;
    private const int NIM_DELETE = 0x00000002;
    private const int NIF_MESSAGE = 0x00000001;
    private const int NIF_ICON = 0x00000002;
    private const int NIF_TIP = 0x00000004;

    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x0010;

    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;
    private const uint MF_GRAYED = 0x0001;

    private const int ID_OPEN = 1001;
    private const int ID_EXIT = 1002;
    private const int ID_PROVIDER_START = 2000;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _iconHandle;
    private readonly bool _ownsIconHandle;
    private readonly WndProc _wndProc;
    private readonly IntPtr _oldWndProc;
    private string _tooltip = "Win Codex Bar";
    private IReadOnlyList<string> _providerMenuLines = Array.Empty<string>();
    private readonly HashSet<int> _providerMenuIds = new();
    private bool _disposed;

    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public TrayService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _iconHandle = LoadTrayIcon(out _ownsIconHandle);
        _wndProc = WindowProc;
        _oldWndProc = SetWindowLongPtr(_hwnd, GWL_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProc));

        AddTrayIcon();
    }

    public void UpdateTooltip(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _tooltip = text.Length <= 63 ? text : text[..63];
        ModifyTrayIcon();
    }

    public void UpdateUsageSummary(UsageSummary summary)
    {
        if (summary.ProviderSnapshots.Count == 0)
        {
            _providerMenuLines = [];
            return;
        }

        _providerMenuLines = summary.ProviderSnapshots
            .OrderBy(snapshot => snapshot.Provider)
            .Select(FormatProviderLine)
            .ToList();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RemoveTrayIcon();
        if (_ownsIconHandle && _iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
        }
        SetWindowLongPtr(_hwnd, GWL_WNDPROC, _oldWndProc);
    }

    private void AddTrayIcon()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _iconHandle,
            szTip = _tooltip
        };

        Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private void ModifyTrayIcon()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_TIP,
            szTip = _tooltip
        };

        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void RemoveTrayIcon()
    {
        var data = new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1
        };

        Shell_NotifyIcon(NIM_DELETE, ref data);
    }

    private IntPtr WindowProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_TRAYICON)
        {
            var eventId = lParam.ToInt32();
            if (eventId == WM_LBUTTONDBLCLK)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
            else if (eventId == WM_RBUTTONUP)
            {
                ShowContextMenu();
            }

            return IntPtr.Zero;
        }

        if (msg == WM_COMMAND)
        {
            var commandId = wParam.ToInt32() & 0xFFFF;
            if (_providerMenuIds.Contains(commandId))
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }

            switch (commandId)
            {
                case ID_OPEN:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    return IntPtr.Zero;
                case ID_EXIT:
                    ExitRequested?.Invoke(this, EventArgs.Empty);
                    return IntPtr.Zero;
            }
        }

        return CallWindowProc(_oldWndProc, hWnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        _providerMenuIds.Clear();
        var nextId = ID_PROVIDER_START;
        if (_providerMenuLines.Count == 0)
        {
            AppendMenu(menu, MF_STRING | MF_GRAYED, ID_PROVIDER_START, "No provider data");
        }
        else
        {
            foreach (var line in _providerMenuLines)
            {
                AppendMenu(menu, MF_STRING, nextId, line);
                _providerMenuIds.Add(nextId);
                nextId++;
            }
        }

        AppendMenu(menu, MF_SEPARATOR, 0, string.Empty);
        AppendMenu(menu, MF_STRING, ID_OPEN, "Open");
        AppendMenu(menu, MF_SEPARATOR, 0, string.Empty);
        AppendMenu(menu, MF_STRING, ID_EXIT, "Exit");

        GetCursorPos(out var point);
        SetForegroundWindow(_hwnd);
        var command = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.X, point.Y, _hwnd, IntPtr.Zero);
        if (command != 0)
        {
            PostMessage(_hwnd, WM_COMMAND, new IntPtr(command), IntPtr.Zero);
        }

        DestroyMenu(menu);
    }

    private static string FormatProviderLine(ProviderUsageSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            return $"{snapshot.Provider}: {snapshot.Error}";
        }

        var primaryLabel = snapshot.Primary?.Label ?? "Session";
        var secondaryLabel = snapshot.Secondary?.Label ?? "Weekly";
        var primaryPercent = FormatPercent(snapshot.Primary?.UsedPercent);
        var secondaryPercent = FormatPercent(snapshot.Secondary?.UsedPercent);

        return $"{snapshot.Provider}: {primaryPercent} {primaryLabel}, {secondaryPercent} {secondaryLabel}";
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:0}%" : "--%";
    }

    private static IntPtr LoadTrayIcon(out bool ownsHandle)
    {
        var iconPath = GetTrayIconPath();
        var handle = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
        if (handle != IntPtr.Zero)
        {
            ownsHandle = true;
            return handle;
        }

        ownsHandle = false;
        return LoadIcon(IntPtr.Zero, new IntPtr(0x7F00));
    }

    private static string GetTrayIconPath()
    {
        try
        {
            return Path.Combine(Package.Current.InstalledLocation.Path, "Assets", "TrayIcon.ico");
        }
        catch (Exception)
        {
            return Path.Combine(AppContext.BaseDirectory, "Assets", "TrayIcon.ico");
        }
    }

    private delegate IntPtr WndProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA data);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr hInstance, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, int uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}


