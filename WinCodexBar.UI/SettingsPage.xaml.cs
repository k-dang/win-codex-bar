using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCodexBar.UI.Services;
using WinCodexBar.UI.ViewModels;

namespace WinCodexBar.UI;

public sealed partial class SettingsPage
{
    private readonly UsageMonitor _monitor;
    public event EventHandler? CloseRequested;

    public SettingsPage(UsageMonitor monitor)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        ViewModel = SettingsViewModel.FromSettings(_monitor.Settings);
        InitializeComponent();
        RootGrid.DataContext = ViewModel;
    }

    public FrameworkElement RootElement => RootGrid;
    public UIElement TitleBarDragRegionElement => TitleBarDragRegion;
    public Border TitleBarRightInsetElement => TitleBarRightInset;
    public SettingsViewModel ViewModel { get; }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _monitor.SaveSettingsAsync(ViewModel.ToSettings());
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Save failed",
                Content = $"Couldn't save settings.\n\nType: {ex.GetType().Name}\nMessage: {ex.Message}",
                CloseButtonText = "OK",
                XamlRoot = RootElement.XamlRoot
            };

            await dialog.ShowAsync();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
