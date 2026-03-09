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

        var providerLines = summary.ProviderSnapshots
            .OrderBy(snapshot => snapshot.Provider)
            .Select(FormatProviderLine)
            .ToList();

        if (providerLines.Count == 0)
        {
            Items.Add(new TrayMenuItem(TrayMenuItemKind.Empty, "No provider data", isEnabled: false));
        }
        else
        {
            foreach (var line in providerLines)
            {
                Items.Add(new TrayMenuItem(TrayMenuItemKind.Provider, line, isEnabled: false));
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

    private static string FormatProviderLine(ProviderUsageSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            return $"{snapshot.Provider}: {snapshot.Error}";
        }

        var primaryLabel = snapshot.Primary?.Label ?? "Session";
        var secondaryLabel = snapshot.Secondary?.Label ?? "Weekly";
        var primaryPercent = FormatPercent(snapshot.Primary?.UsedPercent);
        var secondaryPercent = FormatPercent(snapshot.Secondary?.UsedPercent);

        return $"{snapshot.Provider}: {primaryPercent} {primaryLabel}, {secondaryPercent} {secondaryLabel}";
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:0}%" : "--%";
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
    public TrayMenuItem(TrayMenuItemKind kind, string text, bool isEnabled = true)
    {
        Kind = kind;
        Text = text;
        IsEnabled = isEnabled;
    }

    public TrayMenuItemKind Kind { get; }

    public string Text { get; }

    public bool IsEnabled { get; }
}
