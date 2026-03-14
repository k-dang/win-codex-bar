using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinCodexBar.Core.Models;
using WinCodexBar.UI.Services;

namespace WinCodexBar.UI;

public sealed partial class SettingsPage
{
    private readonly UsageMonitor _monitor;
    public event EventHandler? CloseRequested;

    public SettingsPage(UsageMonitor monitor)
    {
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        InitializeComponent();
        RootGrid.DataContext = this;
        LoadSettings();
    }

    public FrameworkElement RootElement => RootGrid;
    public UIElement TitleBarDragRegionElement => TitleBarDragRegion;
    public Border TitleBarRightInsetElement => TitleBarRightInset;
    public ObservableCollection<ProviderSettingsEditorState> ProviderEditors { get; } = new();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var refreshValue = SettingsRefreshMinutesBox.Value;
        if (double.IsNaN(refreshValue) || refreshValue <= 0)
        {
            refreshValue = 5;
        }

        var settings = new AppSettings
        {
            RefreshMinutes = (int)Math.Max(1, refreshValue),
            Providers = ProviderEditors.ToDictionary(
                editor => editor.Provider,
                editor => new ProviderSettings
                {
                    Enabled = editor.IsEnabled,
                    SourceMode = editor.SelectedSourceMode,
                    CookieSource = editor.SelectedCookieSourceMode,
                    CookieHeader = string.IsNullOrWhiteSpace(editor.CookieHeader) ? null : editor.CookieHeader.Trim()
                })
        };

        try
        {
            await _monitor.SaveSettingsAsync(settings);
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

    private void LoadSettings()
    {
        SettingsRefreshMinutesBox.Value = _monitor.Settings.RefreshMinutes;
        ProviderEditors.Clear();

        foreach (var definition in ProviderCatalog.SupportedProviders)
        {
            var settings = _monitor.Settings.GetProviderSettings(definition.Kind);
            ProviderEditors.Add(new ProviderSettingsEditorState(definition, settings));
        }
    }
}

public sealed class ProviderSettingsEditorState : INotifyPropertyChanged
{
    private bool _isEnabled;
    private int _selectedSourceIndex;
    private int _selectedCookieSourceIndex;
    private string _cookieHeader;

    public ProviderSettingsEditorState(ProviderDefinition definition, ProviderSettings settings)
    {
        Definition = definition;
        SourceModes = definition.SupportedSourceModes.ToArray();
        SourceOptions = SourceModes.Select(ProviderCatalog.GetSourceDisplayName).ToArray();
        CookieSourceModes = new[] { CookieSourceMode.Auto, CookieSourceMode.Manual };
        CookieSourceOptions = CookieSourceModes.Select(ProviderCatalog.GetCookieSourceDisplayName).ToArray();

        _isEnabled = settings.Enabled;
        _selectedSourceIndex = Array.IndexOf(SourceModes, settings.SourceMode);
        if (_selectedSourceIndex < 0)
        {
            _selectedSourceIndex = 0;
        }

        _selectedCookieSourceIndex = Array.IndexOf(CookieSourceModes, settings.CookieSource);
        if (_selectedCookieSourceIndex < 0)
        {
            _selectedCookieSourceIndex = 0;
        }

        _cookieHeader = settings.CookieHeader ?? string.Empty;
    }

    public ProviderDefinition Definition { get; }
    public ProviderKind Provider => Definition.Kind;
    public string SettingsTitle => Definition.SettingsTitle;
    public string EnabledLabel => Definition.EnabledLabel;
    public string SourceLabel => Definition.SourceLabel;
    public string CookieSourceLabel => Definition.CookieSourceLabel;
    public string CookieHeaderPlaceholder => Definition.CookieHeaderPlaceholder;
    public string[] SourceOptions { get; }
    public string[] CookieSourceOptions { get; }
    public ProviderSourceMode[] SourceModes { get; }
    public CookieSourceMode[] CookieSourceModes { get; }
    public Visibility CookieControlsVisibility => Definition.SupportsCookieHeader ? Visibility.Visible : Visibility.Collapsed;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public int SelectedSourceIndex
    {
        get => _selectedSourceIndex;
        set => SetProperty(ref _selectedSourceIndex, value);
    }

    public int SelectedCookieSourceIndex
    {
        get => _selectedCookieSourceIndex;
        set
        {
            if (SetProperty(ref _selectedCookieSourceIndex, value))
            {
                OnPropertyChanged(nameof(IsCookieHeaderEditable));
            }
        }
    }

    public string CookieHeader
    {
        get => _cookieHeader;
        set => SetProperty(ref _cookieHeader, value);
    }

    public ProviderSourceMode SelectedSourceMode =>
        SelectedSourceIndex >= 0 && SelectedSourceIndex < SourceModes.Length
            ? SourceModes[SelectedSourceIndex]
            : ProviderSourceMode.Auto;

    public CookieSourceMode SelectedCookieSourceMode =>
        SelectedCookieSourceIndex >= 0 && SelectedCookieSourceIndex < CookieSourceModes.Length
            ? CookieSourceModes[SelectedCookieSourceIndex]
            : CookieSourceMode.Auto;

    public bool IsCookieHeaderEditable =>
        Definition.SupportsCookieHeader && SelectedCookieSourceMode == CookieSourceMode.Manual;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
