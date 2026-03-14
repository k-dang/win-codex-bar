using WinCodexBar.Core.Models;

namespace WinCodexBar.Tests;

public class AppSettingsTests
{
    [Fact]
    public void CreateDefault_PopulatesProviderMap()
    {
        var settings = AppSettings.CreateDefault();
        var supportedProviders = ProviderCatalog.SupportedProviderKinds.ToArray();

        Assert.NotNull(settings.Providers);
        Assert.Equal(supportedProviders.Length, settings.Providers.Count);

        foreach (var provider in supportedProviders)
        {
            Assert.True(settings.Providers.ContainsKey(provider));
            Assert.NotNull(settings.Providers[provider]);
        }
    }

    [Fact]
    public void NormalizeProviders_ReusesExistingProviderInstances()
    {
        var codexSettings = new ProviderSettings { Enabled = false };
        var settings = new AppSettings
        {
            Providers = new Dictionary<ProviderKind, ProviderSettings>
            {
                [ProviderKind.Codex] = codexSettings
            }
        };

        settings.NormalizeProviders();

        Assert.Same(codexSettings, settings.Providers[ProviderKind.Codex]);
        foreach (var provider in ProviderCatalog.SupportedProviderKinds)
        {
            Assert.NotNull(settings.Providers[provider]);
        }
    }

    [Fact]
    public void NormalizeProviders_SeedsMissingAndNullEntries()
    {
        var settings = new AppSettings
        {
            Providers = null!
        };

        settings.NormalizeProviders();

        Assert.NotNull(settings.Providers);
        foreach (var provider in ProviderCatalog.SupportedProviderKinds)
        {
            Assert.NotNull(settings.Providers[provider]);
        }
    }

    [Fact]
    public void GetProviderSettings_ReturnsExistingOrCreatesDefault()
    {
        var settings = new AppSettings
        {
            Providers = new Dictionary<ProviderKind, ProviderSettings>()
        };

        var codex = settings.GetProviderSettings(ProviderKind.Codex);
        var unknown = settings.GetProviderSettings(ProviderKind.Unknown);

        Assert.Same(codex, settings.Providers[ProviderKind.Codex]);
        Assert.Same(unknown, settings.Providers[ProviderKind.Unknown]);
    }
}

