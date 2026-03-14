using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinCodexBar.Core.Models;

namespace WinCodexBar.UI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int MaxDiagnosticsRows = 40;
    private readonly Dictionary<ProviderKind, List<ProviderUsageRow>> _providerRows = new();
    private ProviderSectionOption? _selectedProviderSection;
    private ProviderKind? _selectedDiagnosticsProvider;

    public MainViewModel()
    {
        foreach (var definition in ProviderCatalog.SupportedProviders)
        {
            ProviderSections.Add(new ProviderSectionOption(definition.Kind, definition.DisplayName, definition.UsageTitle));
            _providerRows[definition.Kind] = new List<ProviderUsageRow>();
        }

        SelectedProviderSection = ProviderSections.FirstOrDefault();
    }

    public ObservableCollection<ProviderSectionOption> ProviderSections { get; } = new();
    public ObservableCollection<ProviderUsageRow> SelectedProviderSnapshots { get; } = new();
    public ObservableCollection<DiagnosticsLogRow> DiagnosticsEntries { get; } = new();
    public ObservableCollection<DiagnosticsLogRow> FilteredDiagnosticsEntries { get; } = new();

    public ProviderSectionOption? SelectedProviderSection
    {
        get => _selectedProviderSection;
        set
        {
            if (SetProperty(ref _selectedProviderSection, value))
            {
                RefreshSelectedProviderSnapshots();
            }
        }
    }

    public ProviderKind? SelectedDiagnosticsProvider
    {
        get => _selectedDiagnosticsProvider;
        set
        {
            if (SetProperty(ref _selectedDiagnosticsProvider, value))
            {
                RefreshFilteredDiagnostics();
            }
        }
    }

    public string SelectedProviderTitle => SelectedProviderSection?.UsageTitle ?? "Provider usage";

    public Visibility SelectedProviderSnapshotsVisibility =>
        SelectedProviderSnapshots.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SelectedProviderEmptyStateVisibility =>
        SelectedProviderSnapshots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SelectProvider(ProviderKind provider)
    {
        SelectedProviderSection = ProviderSections.FirstOrDefault(section => section.Provider == provider)
            ?? SelectedProviderSection;
    }

    public void AddDiagnosticsEntry(DiagnosticsLogEntry entry)
    {
        var row = DiagnosticsLogRow.FromEntry(entry);
        DiagnosticsEntries.Add(row);

        while (DiagnosticsEntries.Count > MaxDiagnosticsRows)
        {
            DiagnosticsEntries.RemoveAt(0);
        }

        RefreshFilteredDiagnostics();
    }

    public void Update(UsageSummary summary)
    {
        foreach (var section in ProviderSections)
        {
            _providerRows[section.Provider].Clear();
        }

        foreach (var snapshot in summary.ProviderSnapshots.OrderBy(item => item.Provider))
        {
            EnsureProviderSection(snapshot.Provider);
            _providerRows[snapshot.Provider].Add(ProviderUsageRow.FromSnapshot(snapshot));
        }

        if (SelectedProviderSection == null && ProviderSections.Count > 0)
        {
            SelectedProviderSection = ProviderSections[0];
            return;
        }

        RefreshSelectedProviderSnapshots();
    }

    private void EnsureProviderSection(ProviderKind provider)
    {
        if (_providerRows.ContainsKey(provider))
        {
            return;
        }

        var definition = ProviderCatalog.GetDefinition(provider);
        ProviderSections.Add(new ProviderSectionOption(definition.Kind, definition.DisplayName, definition.UsageTitle));
        _providerRows[provider] = new List<ProviderUsageRow>();
    }

    private void RefreshSelectedProviderSnapshots()
    {
        SelectedProviderSnapshots.Clear();

        if (SelectedProviderSection != null &&
            _providerRows.TryGetValue(SelectedProviderSection.Provider, out var rows))
        {
            foreach (var row in rows)
            {
                SelectedProviderSnapshots.Add(row);
            }
        }

        OnPropertyChanged(nameof(SelectedProviderTitle));
        OnPropertyChanged(nameof(SelectedProviderSnapshotsVisibility));
        OnPropertyChanged(nameof(SelectedProviderEmptyStateVisibility));
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
        return !_selectedDiagnosticsProvider.HasValue
            || row.ProviderKind == _selectedDiagnosticsProvider
            || row.ProviderKind == null;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed class ProviderSectionOption
{
    public ProviderSectionOption(ProviderKind provider, string displayName, string usageTitle)
    {
        Provider = provider;
        DisplayName = displayName;
        UsageTitle = usageTitle;
    }

    public ProviderKind Provider { get; }
    public string DisplayName { get; }
    public string UsageTitle { get; }
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
        var definition = ProviderCatalog.GetDefinition(snapshot.Provider);
        var primary = snapshot.Primary;
        var secondary = snapshot.Secondary;
        var account = !string.IsNullOrWhiteSpace(snapshot.AccountEmail)
            ? snapshot.AccountEmail
            : snapshot.AccountPlan;

        return new ProviderUsageRow
        {
            ProviderKind = snapshot.Provider,
            ProviderName = definition.DisplayName,
            SourceLabel = snapshot.SourceLabel,
            AccountLabel = account,
            PrimaryLabel = primary?.Label ?? definition.PrimaryUsageLabel,
            PrimaryPercent = primary?.UsedPercent ?? 0,
            PrimaryIndeterminate = primary?.UsedPercent == null,
            PrimaryPercentText = FormatPercent(primary?.UsedPercent),
            PrimaryReset = primary?.ResetDescription ?? string.Empty,
            SecondaryLabel = secondary?.Label ?? definition.SecondaryUsageLabel,
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
    public ProviderKind? ProviderKind { get; init; }
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
            ProviderKind = entry.Provider,
            ProviderName = entry.Provider.HasValue ? ProviderCatalog.GetDisplayName(entry.Provider.Value) : string.Empty,
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
