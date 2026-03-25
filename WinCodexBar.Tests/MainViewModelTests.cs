using Microsoft.UI.Xaml;
using WinCodexBar.Core.Models;
using WinCodexBar.UI;
using WinCodexBar.UI.ViewModels;

namespace WinCodexBar.Tests;

public class MainViewModelTests
{
    [Fact]
    public void Constructor_InitializesProviderSectionsAndSelection()
    {
        var viewModel = new MainViewModel();

        Assert.Equal(ProviderCatalog.SupportedProviders.Count, viewModel.ProviderSections.Count);
        Assert.NotNull(viewModel.SelectedProviderSection);
        Assert.Equal(viewModel.ProviderSections[0], viewModel.SelectedProviderSection);
        Assert.Equal(Visibility.Visible, viewModel.SelectedProviderEmptyStateVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.SelectedProviderSnapshotsVisibility);
    }

    [Fact]
    public void Update_PopulatesSelectedProviderSnapshotsForCurrentSection()
    {
        var viewModel = new MainViewModel();
        var summary = new UsageSummary();
        summary.ProviderSnapshots.Add(new ProviderUsageSnapshot
        {
            Provider = ProviderKind.Codex,
            SourceLabel = "oauth",
            Primary = new UsageWindow { Label = "Session", UsedPercent = 42, ResetDescription = "Resets in 2h" },
            Secondary = new UsageWindow { Label = "Weekly", UsedPercent = 65 }
        });

        viewModel.Update(summary);

        var row = Assert.Single(viewModel.SelectedProviderSnapshots);
        Assert.Equal(ProviderKind.Codex, row.ProviderKind);
        Assert.Equal("Codex", row.ProviderName);
        Assert.Equal("oauth", row.SourceLabel);
        Assert.Equal("42%", row.PrimaryPercentText);
        Assert.Equal("Resets in 2h", row.PrimaryReset);
        Assert.Equal(Visibility.Visible, viewModel.SelectedProviderSnapshotsVisibility);
        Assert.Equal(Visibility.Collapsed, viewModel.SelectedProviderEmptyStateVisibility);
    }

    [Fact]
    public void SelectProvider_SwitchesVisibleRows()
    {
        var viewModel = new MainViewModel();
        var summary = new UsageSummary();
        summary.ProviderSnapshots.Add(new ProviderUsageSnapshot { Provider = ProviderKind.Codex, SourceLabel = "oauth" });
        summary.ProviderSnapshots.Add(new ProviderUsageSnapshot { Provider = ProviderKind.Claude, SourceLabel = "web" });
        viewModel.Update(summary);

        viewModel.SelectProvider(ProviderKind.Claude);

        var row = Assert.Single(viewModel.SelectedProviderSnapshots);
        Assert.Equal(ProviderKind.Claude, row.ProviderKind);
        Assert.Equal("Claude Code", viewModel.SelectedProviderSection!.DisplayName);
    }

    [Fact]
    public void AddDiagnosticsEntry_TrimsRowsAndFiltersByProvider()
    {
        var viewModel = new MainViewModel();

        for (var index = 0; index < 39; index++)
        {
            viewModel.AddDiagnosticsEntry(new DiagnosticsLogEntry
            {
                Provider = ProviderKind.Codex,
                EventType = DiagnosticsEventType.FetchAttempt,
                Message = $"codex-{index}"
            });
        }

        viewModel.AddDiagnosticsEntry(new DiagnosticsLogEntry
        {
            Provider = null,
            EventType = DiagnosticsEventType.RefreshStarted,
            Message = "refresh"
        });

        viewModel.AddDiagnosticsEntry(new DiagnosticsLogEntry
        {
            Provider = ProviderKind.Claude,
            EventType = DiagnosticsEventType.FetchFailure,
            Message = "claude"
        });

        Assert.Equal(40, viewModel.DiagnosticsEntries.Count);
        Assert.Equal("refresh", viewModel.DiagnosticsEntries[^2].Message);
        Assert.Equal("claude", viewModel.DiagnosticsEntries[^1].Message);

        viewModel.SelectedDiagnosticsProvider = ProviderKind.Claude;

        Assert.Equal(2, viewModel.FilteredDiagnosticsEntries.Count);
        Assert.Equal("refresh", viewModel.FilteredDiagnosticsEntries[0].Message);
        Assert.Equal("claude", viewModel.FilteredDiagnosticsEntries[1].Message);
    }

    [Fact]
    public void ProviderSettingsEditorState_UsesManualCookieSelectionForEditability()
    {
        var definition = ProviderCatalog.GetDefinition(ProviderKind.Codex);
        var state = new ProviderSettingsEditorState(definition, new ProviderSettings
        {
            SourceMode = ProviderSourceMode.Cli,
            CookieSource = CookieSourceMode.Manual,
            CookieHeader = "cookie=value"
        });

        Assert.Equal(ProviderSourceMode.Cli, state.SelectedSourceMode);
        Assert.Equal(CookieSourceMode.Manual, state.SelectedCookieSourceMode);
        Assert.True(state.IsCookieHeaderEditable);
        Assert.Equal("cookie=value", state.CookieHeader);

        state.SelectedCookieSourceIndex = 0;

        Assert.Equal(CookieSourceMode.Auto, state.SelectedCookieSourceMode);
        Assert.False(state.IsCookieHeaderEditable);
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
