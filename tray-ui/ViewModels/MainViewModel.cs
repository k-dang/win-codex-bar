using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using tray_ui.Models;

namespace tray_ui.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<ProviderUsageRow> ProviderSnapshots { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(UsageSummary summary)
    {
        ProviderSnapshots.Clear();
        foreach (var snapshot in summary.ProviderSnapshots.OrderBy(item => item.Provider))
        {
            ProviderSnapshots.Add(ProviderUsageRow.FromSnapshot(snapshot));
        }
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class ProviderUsageRow
{
    public string ProviderName { get; init; } = string.Empty;
    public string SourceLabel { get; init; } = string.Empty;
    public string? AccountLabel { get; init; }
    public string PrimaryLabel { get; init; } = "Session";
    public double PrimaryPercent { get; init; }
    public bool PrimaryIndeterminate { get; init; }
    public string PrimaryPercentText { get; init; } = string.Empty;
    public string PrimaryReset { get; init; } = string.Empty;
    public string SecondaryLabel { get; init; } = "Weekly";
    public double SecondaryPercent { get; init; }
    public bool SecondaryIndeterminate { get; init; }
    public string SecondaryPercentText { get; init; } = string.Empty;
    public string SecondaryReset { get; init; } = string.Empty;
    public string? CreditsText { get; init; }
    public string? ErrorText { get; init; }

    public static ProviderUsageRow FromSnapshot(ProviderUsageSnapshot snapshot)
    {
        var primary = snapshot.Primary;
        var secondary = snapshot.Secondary;
        var account = !string.IsNullOrWhiteSpace(snapshot.AccountEmail)
            ? snapshot.AccountEmail
            : snapshot.AccountPlan;

        return new ProviderUsageRow
        {
            ProviderName = snapshot.Provider.ToString(),
            SourceLabel = snapshot.SourceLabel,
            AccountLabel = account,
            PrimaryLabel = primary?.Label ?? "Session",
            PrimaryPercent = primary?.UsedPercent ?? 0,
            PrimaryIndeterminate = primary?.UsedPercent == null,
            PrimaryPercentText = FormatPercent(primary?.UsedPercent),
            PrimaryReset = primary?.ResetDescription ?? string.Empty,
            SecondaryLabel = secondary?.Label ?? "Weekly",
            SecondaryPercent = secondary?.UsedPercent ?? 0,
            SecondaryIndeterminate = secondary?.UsedPercent == null,
            SecondaryPercentText = FormatPercent(secondary?.UsedPercent),
            SecondaryReset = secondary?.ResetDescription ?? string.Empty,
            CreditsText = snapshot.CreditsText,
            ErrorText = snapshot.Error
        };
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:0}%" : "--";
    }
}
