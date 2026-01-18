using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using tray_ui.Models;

namespace tray_ui.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public ObservableCollection<DailyUsage> DailyTotals { get; } = new();
    public ObservableCollection<ProviderUsageRow> ProviderSnapshots { get; } = new();
    public ObservableCollection<DiagnosticsLine> DiagnosticsLines { get; } = new();

    private int _todayTotal;
    private int _last7DaysTotal;
    private int _last30DaysTotal;
    private string _lastSessionSummary = "No sessions yet";

    public int TodayTotal
    {
        get => _todayTotal;
        set => SetProperty(ref _todayTotal, value);
    }

    public int Last7DaysTotal
    {
        get => _last7DaysTotal;
        set => SetProperty(ref _last7DaysTotal, value);
    }

    public int Last30DaysTotal
    {
        get => _last30DaysTotal;
        set => SetProperty(ref _last30DaysTotal, value);
    }

    public string LastSessionSummary
    {
        get => _lastSessionSummary;
        set => SetProperty(ref _lastSessionSummary, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Update(UsageSummary summary)
    {
        DailyTotals.Clear();
        foreach (var item in summary.DailyTotals.OrderByDescending(item => item.Date))
        {
            DailyTotals.Add(item);
        }

        ProviderSnapshots.Clear();
        foreach (var snapshot in summary.ProviderSnapshots.OrderBy(item => item.Provider))
        {
            ProviderSnapshots.Add(ProviderUsageRow.FromSnapshot(snapshot));
        }

        DiagnosticsLines.Clear();
        foreach (var root in summary.LogRoots)
        {
            DiagnosticsLines.Add(new DiagnosticsLine { Label = "Log root", Value = root });
        }
        foreach (var snapshot in summary.ProviderSnapshots.OrderBy(item => item.Provider))
        {
            var status = string.IsNullOrWhiteSpace(snapshot.Error) ? "ok" : "error";
            DiagnosticsLines.Add(new DiagnosticsLine
            {
                Label = $"{snapshot.Provider} source",
                Value = $"{snapshot.SourceLabel} ({status})"
            });
        }
        foreach (var error in summary.ScanErrors)
        {
            DiagnosticsLines.Add(new DiagnosticsLine { Label = "Scan error", Value = error });
        }

        var today = DateTime.Today;
        TodayTotal = summary.DailyTotals
            .Where(item => item.Date == today)
            .Sum(item => item.TotalTokens);

        Last7DaysTotal = summary.DailyTotals
            .Where(item => item.Date >= today.AddDays(-6))
            .Sum(item => item.TotalTokens);

        Last30DaysTotal = summary.DailyTotals
            .Where(item => item.Date >= today.AddDays(-29))
            .Sum(item => item.TotalTokens);

        LastSessionSummary = summary.LastSession == null
            ? "No sessions yet"
            : $"{summary.LastSession.Provider} - {summary.LastSession.TotalTokens} tokens";
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

public sealed class DiagnosticsLine
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
}
