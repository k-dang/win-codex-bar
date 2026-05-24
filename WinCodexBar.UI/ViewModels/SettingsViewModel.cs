using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using WinCodexBar.Core.Models;

namespace WinCodexBar.UI.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private double _refreshMinutes;

    public double RefreshMinutes
    {
        get => _refreshMinutes;
        set => SetProperty(ref _refreshMinutes, value);
    }

    public ObservableCollection<ProviderSettingsEditorState> ProviderEditors { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public static SettingsViewModel FromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var viewModel = new SettingsViewModel
        {
            RefreshMinutes = settings.RefreshMinutes > 0 ? settings.RefreshMinutes : AppSettings.CreateDefault().RefreshMinutes
        };

        foreach (var definition in ProviderCatalog.SupportedProviders)
        {
            var providerSettings = settings.GetProviderSettings(definition.Kind);
            viewModel.ProviderEditors.Add(new ProviderSettingsEditorState(definition, providerSettings));
        }

        return viewModel;
    }

    public AppSettings ToSettings()
    {
        var refreshValue = RefreshMinutes;
        if (double.IsNaN(refreshValue) || refreshValue <= 0)
        {
            refreshValue = AppSettings.CreateDefault().RefreshMinutes;
        }

        return new AppSettings
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
    }

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
