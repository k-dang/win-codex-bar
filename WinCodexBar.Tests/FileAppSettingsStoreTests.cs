using System.Text.Json;
using WinCodexBar.Core.Models;
using WinCodexBar.UI.Services;

namespace WinCodexBar.Tests;

public sealed class FileAppSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_MissingFile_ReturnsDefaultSettings()
    {
        var store = new FileAppSettingsStore(CreateSettingsPath());

        var settings = await store.LoadAsync();

        Assert.Equal(5, settings.RefreshMinutes);
        foreach (var provider in ProviderCatalog.SupportedProviderKinds)
        {
            Assert.NotNull(settings.GetProviderSettings(provider));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ invalid json")]
    public async Task LoadAsync_EmptyOrInvalidJson_ReturnsDefaultSettings(string json)
    {
        var path = CreateSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json);
        var store = new FileAppSettingsStore(path);

        var settings = await store.LoadAsync();

        Assert.Equal(5, settings.RefreshMinutes);
        Assert.True(settings.GetProviderSettings(ProviderKind.Codex).Enabled);
    }

    [Fact]
    public async Task LoadAsync_InvalidRefreshMinutes_UsesDefaultRefreshMinutesAndNormalizesProviders()
    {
        var path = CreateSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            {
              "RefreshMinutes": 0,
              "Providers": {
                "Codex": {
                  "Enabled": false,
                  "SourceMode": 3,
                  "CookieSource": 1,
                  "CookieHeader": "codex-cookie"
                }
              }
            }
            """);
        var store = new FileAppSettingsStore(path);

        var settings = await store.LoadAsync();

        Assert.Equal(5, settings.RefreshMinutes);
        Assert.False(settings.GetProviderSettings(ProviderKind.Codex).Enabled);
        Assert.Equal(ProviderSourceMode.Cli, settings.GetProviderSettings(ProviderKind.Codex).SourceMode);
        Assert.Equal("codex-cookie", settings.GetProviderSettings(ProviderKind.Codex).CookieHeader);
        Assert.NotNull(settings.GetProviderSettings(ProviderKind.Claude));
    }

    [Fact]
    public async Task SaveAsync_NormalizesProvidersBeforeWritingAndReturnsSavedSettings()
    {
        var path = CreateSettingsPath();
        var store = new FileAppSettingsStore(path);
        var settings = new AppSettings
        {
            RefreshMinutes = 10,
            Providers = new Dictionary<ProviderKind, ProviderSettings>
            {
                [ProviderKind.Codex] = new() { Enabled = false }
            }
        };

        var saved = await store.SaveAsync(settings);

        Assert.Same(settings, saved);
        Assert.NotNull(saved.GetProviderSettings(ProviderKind.Claude));
        Assert.True(File.Exists(path));

        var json = await File.ReadAllTextAsync(path);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty(nameof(AppSettings.RefreshMinutes), out var refreshMinutes));
        Assert.Equal(10, refreshMinutes.GetInt32());
        Assert.Contains(Environment.NewLine, json);
    }

    private static string CreateSettingsPath()
    {
        return Path.Combine(Path.GetTempPath(), "WinCodexBar.Tests", Guid.NewGuid().ToString("N"), "settings.json");
    }
}
