using WinCodexBar.Core.Models;
using WinCodexBar.UI.ViewModels;

namespace WinCodexBar.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void FromSettings_CreatesEditorsForSupportedProviders()
    {
        var settings = AppSettings.CreateDefault();
        settings.RefreshMinutes = 12;
        settings.GetProviderSettings(ProviderKind.Codex).Enabled = false;

        var viewModel = SettingsViewModel.FromSettings(settings);

        Assert.Equal(12, viewModel.RefreshMinutes);
        Assert.Equal(ProviderCatalog.SupportedProviders.Count, viewModel.ProviderEditors.Count);
        Assert.False(viewModel.ProviderEditors.Single(editor => editor.Provider == ProviderKind.Codex).IsEnabled);
    }

    [Fact]
    public void ToSettings_ClampsInvalidRefreshMinutesAndTrimsCookieHeaders()
    {
        var viewModel = SettingsViewModel.FromSettings(AppSettings.CreateDefault());
        viewModel.RefreshMinutes = double.NaN;
        var codexEditor = viewModel.ProviderEditors.Single(editor => editor.Provider == ProviderKind.Codex);
        codexEditor.SelectedSourceIndex = Array.IndexOf(codexEditor.SourceModes, ProviderSourceMode.Cli);
        codexEditor.SelectedCookieSourceIndex = Array.IndexOf(codexEditor.CookieSourceModes, CookieSourceMode.Manual);
        codexEditor.CookieHeader = "  cookie=value  ";

        var settings = viewModel.ToSettings();

        var codexSettings = settings.GetProviderSettings(ProviderKind.Codex);
        Assert.Equal(5, settings.RefreshMinutes);
        Assert.Equal(ProviderSourceMode.Cli, codexSettings.SourceMode);
        Assert.Equal(CookieSourceMode.Manual, codexSettings.CookieSource);
        Assert.Equal("cookie=value", codexSettings.CookieHeader);
    }

    [Fact]
    public void ToSettings_ConvertsEmptyCookieHeaderToNull()
    {
        var viewModel = SettingsViewModel.FromSettings(AppSettings.CreateDefault());
        var codexEditor = viewModel.ProviderEditors.Single(editor => editor.Provider == ProviderKind.Codex);
        codexEditor.SelectedCookieSourceIndex = Array.IndexOf(codexEditor.CookieSourceModes, CookieSourceMode.Manual);
        codexEditor.CookieHeader = "   ";

        var settings = viewModel.ToSettings();

        Assert.Null(settings.GetProviderSettings(ProviderKind.Codex).CookieHeader);
    }

    [Fact]
    public void ProviderSettingsEditorState_FallsBackToAutoForUnknownIndexes()
    {
        var definition = ProviderCatalog.GetDefinition(ProviderKind.Codex);
        var state = new ProviderSettingsEditorState(definition, new ProviderSettings
        {
            SourceMode = (ProviderSourceMode)999,
            CookieSource = (CookieSourceMode)999
        });

        Assert.Equal(0, state.SelectedSourceIndex);
        Assert.Equal(0, state.SelectedCookieSourceIndex);
        Assert.Equal(ProviderSourceMode.Auto, state.SelectedSourceMode);
        Assert.Equal(CookieSourceMode.Auto, state.SelectedCookieSourceMode);
    }
}
