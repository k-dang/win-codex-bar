using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using WinCodexBar.Core.Models;

namespace WinCodexBar.UI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _selectedProviderFilter = "All";

    public ObservableCollection<ProviderUsageRow> ProviderSnapshots { get; } = new();
    public ObservableCollection<ProviderUsageRow> CodexSnapshots { get; } = new();
    public ObservableCollection<ProviderUsageRow> ClaudeSnapshots { get; } = new();
    public ObservableCollection<DiagnosticsLogRow> DiagnosticsEntries { get; } = new();
    public ObservableCollection<DiagnosticsLogRow> FilteredDiagnosticsEntries { get; } = new();

    public string SelectedProviderFilter
    {
        get => _selectedProviderFilter;
        set
        {
            if (SetProperty(ref _selectedProviderFilter, value))
            {
                RefreshFilteredDiagnostics();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void AddDiagnosticsEntry(DiagnosticsLogEntry entry)
    {
        var row = DiagnosticsLogRow.FromEntry(entry);
        DiagnosticsEntries.Add(row);
        if (PassesFilter(row))
        {
            FilteredDiagnosticsEntries.Add(row);
        }
    }

    private void RefreshFilteredDiagnostics()
    {
        FilteredDiagnosticsEntries.Clear();
        foreach (var entry in DiagnosticsEntries.Where(PassesFilter))
        {
            FilteredDiagnosticsEntries.Add(entry);
        }
    }

    private bool PassesFilter(DiagnosticsLogRow row)
    {
        if (_selectedProviderFilter == "All")
        {
            return true;
        }
        return string.Equals(row.ProviderName, _selectedProviderFilter, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(row.ProviderName);
    }

    public void Update(UsageSummary summary)
    {
        ProviderSnapshots.Clear();
        CodexSnapshots.Clear();
        ClaudeSnapshots.Clear();

        foreach (var snapshot in summary.ProviderSnapshots.OrderBy(item => item.Provider))
        {
            var row = ProviderUsageRow.FromSnapshot(snapshot);
            ProviderSnapshots.Add(row);

            if (snapshot.Provider == ProviderKind.Codex)
            {
                CodexSnapshots.Add(row);
            }
            else if (snapshot.Provider == ProviderKind.Claude)
            {
                ClaudeSnapshots.Add(row);
            }
        }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}

public sealed class ProviderUsageRow
{
    public ProviderKind ProviderKind { get; init; }
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
    public bool HasError { get; init; }

    public static ProviderUsageRow FromSnapshot(ProviderUsageSnapshot snapshot)
    {
        var primary = snapshot.Primary;
        var secondary = snapshot.Secondary;
        var account = !string.IsNullOrWhiteSpace(snapshot.AccountEmail)
            ? snapshot.AccountEmail
            : snapshot.AccountPlan;

        return new ProviderUsageRow
        {
            ProviderKind = snapshot.Provider,
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
            ErrorText = snapshot.Error,
            HasError = !string.IsNullOrWhiteSpace(snapshot.Error)
        };
    }

    private static string FormatPercent(double? value)
    {
        return value.HasValue ? $"{value.Value:0}%" : "--";
    }
}

public sealed class DiagnosticsLogRow
{
    private static readonly SolidColorBrush ErrorBrush = new(Microsoft.UI.Colors.Red);
    private static readonly SolidColorBrush NormalBrush = new(Microsoft.UI.Colors.White);

    public string TimestampText { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string EventTypeName { get; init; } = string.Empty;
    public string SourceMethod { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string DurationText { get; init; } = string.Empty;
    public bool IsError { get; init; }
    public Brush MessageForeground { get; init; } = NormalBrush;

    public static DiagnosticsLogRow FromEntry(DiagnosticsLogEntry entry)
    {
        var isError = entry.EventType == DiagnosticsEventType.FetchFailure;
        var duration = entry.Duration.HasValue
            ? $"{entry.Duration.Value.TotalMilliseconds:0}ms"
            : string.Empty;

        return new DiagnosticsLogRow
        {
            TimestampText = entry.Timestamp.ToString("HH:mm:ss.fff"),
            ProviderName = entry.Provider?.ToString() ?? string.Empty,
            EventTypeName = FormatEventType(entry.EventType),
            SourceMethod = entry.SourceMethod ?? string.Empty,
            Message = entry.Message,
            DurationText = duration,
            IsError = isError,
            MessageForeground = isError ? ErrorBrush : NormalBrush
        };
    }

    private static string FormatEventType(DiagnosticsEventType eventType)
    {
        return eventType switch
        {
            DiagnosticsEventType.FetchAttempt => "Attempt",
            DiagnosticsEventType.FetchSuccess => "Success",
            DiagnosticsEventType.FetchFailure => "Failure",
            DiagnosticsEventType.RefreshStarted => "Started",
            DiagnosticsEventType.RefreshCompleted => "Completed",
            _ => eventType.ToString()
        };
    }
}

