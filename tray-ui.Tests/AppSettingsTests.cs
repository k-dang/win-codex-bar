using System.Collections.Generic;
using tray_ui.Models;
using Xunit;

namespace tray_ui.Tests;

public class AppSettingsTests
{
    [Fact]
    public void CreateDefault_PopulatesProviderMap()
    {
        var settings = AppSettings.CreateDefault();

        Assert.NotNull(settings.Providers);
        Assert.Equal(2, settings.Providers.Count);
        Assert.Same(settings.Codex, settings.Providers[ProviderKind.Codex]);
        Assert.Same(settings.Claude, settings.Providers[ProviderKind.Claude]);
    }

    [Fact]
    public void NormalizeProviders_ReusesExistingProviderInstances()
    {
        var codexSettings = new ProviderSettings { Enabled = false };
        var settings = new AppSettings
        {
            Codex = ProviderSettings.CreateDefault(ProviderKind.Codex),
            Claude = ProviderSettings.CreateDefault(ProviderKind.Claude),
            Providers = new Dictionary<ProviderKind, ProviderSettings>
            {
                [ProviderKind.Codex] = codexSettings
            }
        };

        settings.NormalizeProviders();

        Assert.Same(codexSettings, settings.Codex);
        Assert.NotNull(settings.Providers[ProviderKind.Claude]);
    }

    [Fact]
    public void NormalizeProviders_SeedsMissingAndNullEntries()
    {
        var settings = new AppSettings
        {
            Providers = null!,
            Codex = null!,
            Claude = null!
        };

        settings.NormalizeProviders();

        Assert.NotNull(settings.Providers);
        Assert.NotNull(settings.Codex);
        Assert.NotNull(settings.Claude);
        Assert.NotNull(settings.Providers[ProviderKind.Codex]);
        Assert.NotNull(settings.Providers[ProviderKind.Claude]);
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
