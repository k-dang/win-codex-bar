using WinCodexBar.Core.Models;
using WinCodexBar.UI.ViewModels;

namespace WinCodexBar.Tests;

public class TrayMenuViewModelTests
{
    [Fact]
    public void Update_CreatesStructuredProviderItemsForUsageRows()
    {
        var viewModel = new TrayMenuViewModel();
        var summary = new UsageSummary();
        summary.ProviderSnapshots.Add(new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Codex,
            Primary = new UsageWindow { Label = "Session", UsedPercent = 34.2 },
            Secondary = new UsageWindow { Label = "Weekly", UsedPercent = 76.8 }
        });

        viewModel.Update(summary);

        var providerItem = Assert.Single(viewModel.Items, item => item.Kind == TrayMenuItemKind.Provider);
        Assert.Equal("Codex", providerItem.Text);
        Assert.Null(providerItem.ErrorText);
        Assert.NotNull(providerItem.PrimaryMetric);
        Assert.NotNull(providerItem.SecondaryMetric);
        Assert.Equal("Session", providerItem.PrimaryMetric!.Label);
        Assert.Equal(34.2, providerItem.PrimaryMetric.PercentValue);
        Assert.Equal("34%", providerItem.PrimaryMetric.PercentText);
        Assert.Equal("Weekly", providerItem.SecondaryMetric!.Label);
        Assert.Equal(76.8, providerItem.SecondaryMetric.PercentValue);
        Assert.Equal("77%", providerItem.SecondaryMetric.PercentText);
    }

    [Fact]
    public void Update_UsesUnavailablePercentTextWhenMetricValuesAreMissing()
    {
        var viewModel = new TrayMenuViewModel();
        var summary = new UsageSummary();
        summary.ProviderSnapshots.Add(new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Claude,
            Primary = new UsageWindow { Label = "Session", UsedPercent = null },
            Secondary = null
        });

        viewModel.Update(summary);

        var providerItem = Assert.Single(viewModel.Items, item => item.Kind == TrayMenuItemKind.Provider);
        Assert.Equal("--%", providerItem.PrimaryMetric!.PercentText);
        Assert.Null(providerItem.PrimaryMetric.PercentValue);
        Assert.Equal("Weekly", providerItem.SecondaryMetric!.Label);
        Assert.Equal("--%", providerItem.SecondaryMetric.PercentText);
        Assert.Null(providerItem.SecondaryMetric.PercentValue);
    }

    [Fact]
    public void Update_CreatesProviderErrorItemsWhenSnapshotHasError()
    {
        var viewModel = new TrayMenuViewModel();
        var summary = new UsageSummary();
        summary.ProviderSnapshots.Add(new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Codex,
            Error = "Timed out"
        });

        viewModel.Update(summary);

        var providerItem = Assert.Single(viewModel.Items, item => item.Kind == TrayMenuItemKind.Provider);
        Assert.Equal("Codex", providerItem.Text);
        Assert.Equal("Timed out", providerItem.ErrorText);
        Assert.Null(providerItem.PrimaryMetric);
        Assert.Null(providerItem.SecondaryMetric);
    }

    [Fact]
    public void Update_KeepsEmptyStateAndActionsWhenNoProvidersExist()
    {
        var viewModel = new TrayMenuViewModel();

        viewModel.Update(new UsageSummary());

        Assert.Contains(viewModel.Items, item => item.Kind == TrayMenuItemKind.Empty && item.Text == "No provider data");
        Assert.Contains(viewModel.Items, item => item.Kind == TrayMenuItemKind.Open && item.Text == "Open");
        Assert.Contains(viewModel.Items, item => item.Kind == TrayMenuItemKind.Exit && item.Text == "Exit");
    }
}
