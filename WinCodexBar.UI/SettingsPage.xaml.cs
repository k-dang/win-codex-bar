using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCodexBar.Core.Models;
using WinCodexBar.UI.Services;

namespace WinCodexBar.UI;

public sealed partial class SettingsPage
{
    private readonly UsageMonitor _monitor;
    public event EventHandler? CloseRequested;

    public FrameworkElement RootElement => RootGrid;
    public UIElement TitleBarDragRegionElement => TitleBarDragRegion;
    public Border TitleBarRightInsetElement => TitleBarRightInset;

    public SettingsPage(UsageMonitor monitor)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        InitializeComponent();
        LoadSettings();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var refreshValue = SettingsRefreshMinutesBox.Value;
        if (double.IsNaN(refreshValue) || refreshValue <= 0)
        {
            refreshValue = 5;
        }

        var codexSettings = new ProviderSettings
        {
            Enabled = SettingsCodexEnabledBox.IsChecked == true,
            SourceMode = ParseSourceMode(SettingsCodexSourceBox.SelectedIndex),
            CookieSource = ParseCookieSource(SettingsCodexCookieSourceBox.SelectedIndex),
            CookieHeader = SettingsCodexCookieHeaderBox.Text?.Trim()
        };

        var claudeSettings = new ProviderSettings
        {
            Enabled = SettingsClaudeEnabledBox.IsChecked == true,
            SourceMode = ParseSourceMode(SettingsClaudeSourceBox.SelectedIndex),
            CookieSource = ParseCookieSource(SettingsClaudeCookieSourceBox.SelectedIndex),
            CookieHeader = SettingsClaudeCookieHeaderBox.Text?.Trim()
        };

        var settings = new AppSettings
        {
            RefreshMinutes = (int)Math.Max(1, refreshValue),
            Codex = codexSettings,
            Claude = claudeSettings
        };

        await _monitor.SaveSettingsAsync(settings);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
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

    private static ProviderSourceMode ParseSourceMode(int selectedIndex)
    {
        return selectedIndex switch
        {
            1 => ProviderSourceMode.OAuth,
            2 => ProviderSourceMode.Web,
            3 => ProviderSourceMode.Cli,
            _ => ProviderSourceMode.Auto
        };
    }

    private static int SourceIndex(ProviderSourceMode mode)
    {
        return mode switch
        {
            ProviderSourceMode.OAuth => 1,
            ProviderSourceMode.Web => 2,
            ProviderSourceMode.Cli => 3,
            _ => 0
        };
    }

    private static CookieSourceMode ParseCookieSource(int selectedIndex)
    {
        return selectedIndex == 1 ? CookieSourceMode.Manual : CookieSourceMode.Auto;
    }

    private static int CookieIndex(CookieSourceMode mode)
    {
        return mode == CookieSourceMode.Manual ? 1 : 0;
    }
}

