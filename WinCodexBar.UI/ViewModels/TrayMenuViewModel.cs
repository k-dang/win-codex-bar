using System;
using System.Collections.ObjectModel;
using System.Linq;
using WinCodexBar.Core.Models;

namespace WinCodexBar.UI.ViewModels;

public sealed class TrayMenuViewModel
{
    public ObservableCollection<TrayMenuItem> Items { get; } = new();

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public void Update(UsageSummary summary)
    {
        Items.Clear();

        var providerItems = summary.ProviderSnapshots
            .OrderBy(snapshot => snapshot.Provider)
            .Select(CreateProviderItem)
            .ToList();

        if (providerItems.Count == 0)
        {
            Items.Add(new TrayMenuItem(TrayMenuItemKind.Empty, "No provider data", isEnabled: false));
        }
        else
        {
            foreach (var item in providerItems)
            {
                Items.Add(item);
            }
        }

        Items.Add(new TrayMenuItem(TrayMenuItemKind.Separator, string.Empty, isEnabled: false));
        Items.Add(new TrayMenuItem(TrayMenuItemKind.Open, "Open"));
        Items.Add(new TrayMenuItem(TrayMenuItemKind.Separator, string.Empty, isEnabled: false));
        Items.Add(new TrayMenuItem(TrayMenuItemKind.Exit, "Exit"));
    }

    public void ActivateItem(TrayMenuItem item)
    {
        switch (item.Kind)
        {
            case TrayMenuItemKind.Open:
                OpenRequested?.Invoke(this, EventArgs.Empty);
                break;
            case TrayMenuItemKind.Exit:
                ExitRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private static TrayMenuItem CreateProviderItem(ProviderUsageSnapshot snapshot)
    {
        var providerName = ProviderCatalog.GetDisplayName(snapshot.Provider);

        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            return TrayMenuItem.CreateProviderError(providerName, snapshot.Error);
        }

        var definition = ProviderCatalog.GetDefinition(snapshot.Provider);

        return TrayMenuItem.CreateProvider(
            providerName,
            CreateMetric(snapshot.Primary, definition.PrimaryUsageLabel),
            CreateMetric(snapshot.Secondary, definition.SecondaryUsageLabel));
    }

    private static TrayMenuMetric CreateMetric(UsageWindow? window, string fallbackLabel)
    {
        var value = window?.UsedPercent;
        return new TrayMenuMetric(
            window?.Label ?? fallbackLabel,
            value,
            value.HasValue ? $"{value.Value:0}%" : "--%");
    }
}

public enum TrayMenuItemKind
{
    Provider,
    Open,
    Exit,
    Separator,
    Empty
}

public sealed class TrayMenuItem
{
    public TrayMenuItem(
        TrayMenuItemKind kind,
        string text,
        bool isEnabled = true,
        TrayMenuMetric? primaryMetric = null,
        TrayMenuMetric? secondaryMetric = null,
        string? errorText = null)
    {
        Kind = kind;
        Text = text;
        IsEnabled = isEnabled;
        PrimaryMetric = primaryMetric;
        SecondaryMetric = secondaryMetric;
        ErrorText = errorText;
    }

    public TrayMenuItemKind Kind { get; }

    public string Text { get; }

    public bool IsEnabled { get; }

    public TrayMenuMetric? PrimaryMetric { get; }

    public TrayMenuMetric? SecondaryMetric { get; }

    public string? ErrorText { get; }

    public static TrayMenuItem CreateProvider(string providerName, TrayMenuMetric primaryMetric, TrayMenuMetric secondaryMetric)
    {
        return new TrayMenuItem(
            TrayMenuItemKind.Provider,
            providerName,
            isEnabled: false,
            primaryMetric: primaryMetric,
            secondaryMetric: secondaryMetric);
    }

    public static TrayMenuItem CreateProviderError(string providerName, string errorText)
    {
        return new TrayMenuItem(
            TrayMenuItemKind.Provider,
            providerName,
            isEnabled: false,
            errorText: errorText);
    }
}

public sealed class TrayMenuMetric
{
    public TrayMenuMetric(string label, double? percentValue, string percentText)
    {
        Label = label;
        PercentValue = percentValue;
        PercentText = percentText;
    }

    public string Label { get; }

    public double? PercentValue { get; }

    public string PercentText { get; }
}
