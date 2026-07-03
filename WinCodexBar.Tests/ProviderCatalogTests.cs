using WinCodexBar.Core.Models;

namespace WinCodexBar.Tests;

public class ProviderCatalogTests
{
    [Fact]
    public void GetDefinition_ReturnsFallbackDefinitionForUnknownProvider()
    {
        var definition = ProviderCatalog.GetDefinition(ProviderKind.Unknown);

        Assert.Equal(ProviderKind.Unknown, definition.Kind);
        Assert.Equal("Unknown", definition.DisplayName);
        Assert.Equal("Unknown usage", definition.UsageTitle);
        Assert.True(definition.SupportsCookieHeader);
        Assert.True(definition.EnabledByDefault);
        Assert.Contains(ProviderSourceMode.Auto, definition.SupportedSourceModes);
    }

    [Fact]
    public void GetDefinition_Cursor_UsesTotalAutoLabelsAndDefaultDisabled()
    {
        var definition = ProviderCatalog.GetDefinition(ProviderKind.Cursor);

        Assert.Equal("Cursor", definition.DisplayName);
        Assert.Equal("Total", definition.PrimaryUsageLabel);
        Assert.Equal("Auto", definition.SecondaryUsageLabel);
        Assert.False(definition.EnabledByDefault);
        Assert.True(definition.SupportsCookieHeader);
        Assert.Equal(
            new[] { ProviderSourceMode.Auto, ProviderSourceMode.OAuth, ProviderSourceMode.Web },
            definition.SupportedSourceModes);
    }

    [Theory]
    [InlineData(ProviderSourceMode.Auto, "Auto")]
    [InlineData(ProviderSourceMode.OAuth, "OAuth")]
    [InlineData(ProviderSourceMode.Web, "Web (Cookies)")]
    [InlineData(ProviderSourceMode.Cli, "CLI")]
    public void GetSourceDisplayName_ReturnsExpectedLabels(ProviderSourceMode mode, string expected)
    {
        Assert.Equal(expected, ProviderCatalog.GetSourceDisplayName(mode));
    }

    [Theory]
    [InlineData(CookieSourceMode.Auto, "Auto")]
    [InlineData(CookieSourceMode.Manual, "Manual")]
    public void GetCookieSourceDisplayName_ReturnsExpectedLabels(CookieSourceMode mode, string expected)
    {
        Assert.Equal(expected, ProviderCatalog.GetCookieSourceDisplayName(mode));
    }

    [Fact]
    public void CreateDefaultProviderSettings_UsesEnabledAutoModes()
    {
        var settings = ProviderSettings.CreateDefault(ProviderKind.Codex);

        Assert.True(settings.Enabled);
        Assert.Equal(ProviderSourceMode.Auto, settings.SourceMode);
        Assert.Equal(CookieSourceMode.Auto, settings.CookieSource);
        Assert.Null(settings.CookieHeader);
    }

    [Fact]
    public void CreateDefaultProviderSettings_DefaultDisabledProvider_ProducesDisabledSettings()
    {
        var settings = ProviderSettings.CreateDefault(ProviderKind.Cursor);

        Assert.False(settings.Enabled);
        Assert.Equal(ProviderSourceMode.Auto, settings.SourceMode);
        Assert.Equal(CookieSourceMode.Auto, settings.CookieSource);
    }
}
