using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using Windows.Graphics;
using Microsoft.UI;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace tray_ui;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;

        SetTitleBar(AppTitleBar);
        // Set the initial window size (width, height)
        SetInitialWindowSize(900, 600);
    }

    private void AppTitleBar_BackRequested(TitleBar sender, object args)
    {
        if (rootFrame.CanGoBack == true)
        {
            rootFrame.GoBack();
        }
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
                var size = new SizeInt32 { Width = width, Height = height };
                appWindow.Resize(size);
            }
        }
        catch
        {
            // If the Windowing APIs aren't available at runtime, silently ignore.
        }
    }
}