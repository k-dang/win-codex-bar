## Problem

Settings behavior is currently spread across several shallow modules:

- `AppSettings` creates defaults and mutates itself to normalize provider entries.
- `SettingsStore` chooses the persisted JSON path, handles corrupt/missing files, performs refresh interval validation, and normalizes providers before saving.
- `SettingsPage` reads live monitor settings, constructs provider editor state, converts UI selections back into `AppSettings`, trims cookie headers, and handles save failures.
- `UsageMonitor` applies saved settings by replacing the current settings object, persisting it, and reconfiguring the refresh timer.

The integration risk is in the seams between those responsibilities. A change to provider defaults, refresh interval validation, cookie handling, or JSON compatibility requires checking model code, persistence code, page code-behind, and monitor behavior together. The current tests cover pieces of this flow, but there is no boundary test for the actual settings transaction: load settings, present editable state, save normalized settings, and apply the result.

This also makes the app harder to navigate. The settings page looks like UI code, but it owns domain mapping. The store looks like persistence code, but it owns validation policy. The monitor looks like runtime orchestration, but it also owns part of the settings lifecycle.

## Proposed Interface

Introduce a deeper settings boundary with two parts:

```csharp
public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync();
    Task<AppSettings> SaveAsync(AppSettings settings);
}

public sealed class FileAppSettingsStore : IAppSettingsStore
{
    public FileAppSettingsStore(string settingsPath);

    public Task<AppSettings> LoadAsync();
    public Task<AppSettings> SaveAsync(AppSettings settings);
}
```

```csharp
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public int RefreshMinutes { get; set; }
    public ObservableCollection<ProviderSettingsEditorState> ProviderEditors { get; }

    public static SettingsViewModel FromSettings(AppSettings settings);
    public AppSettings ToSettings();
}
```

Usage from the settings page becomes:

```csharp
public SettingsPage(UsageMonitor monitor)
{
    _monitor = monitor;
    ViewModel = SettingsViewModel.FromSettings(_monitor.Settings);
    InitializeComponent();
    RootGrid.DataContext = ViewModel;
}

private async void Save_Click(object sender, RoutedEventArgs e)
{
    try
    {
        await _monitor.SaveSettingsAsync(ViewModel.ToSettings());
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
    catch (Exception ex)
    {
        await ShowSaveFailureAsync(ex);
    }
}
```

Usage from the monitor becomes:

```csharp
public async Task InitializeAsync()
{
    ApplySettings(await _settingsStore.LoadAsync());
    await RefreshAsync();
}

public async Task SaveSettingsAsync(AppSettings settings)
{
    ApplySettings(await _settingsStore.SaveAsync(settings));
}

private void ApplySettings(AppSettings settings)
{
    _settings = settings;
    ConfigureTimer();
}
```

The complexity hidden internally:

- Default settings creation.
- Missing, null, or future provider entries.
- Invalid refresh interval fallback.
- JSON file path selection and corrupt/empty file behavior.
- Save-time normalization.
- Cookie header trimming and empty-to-null conversion.
- Editor index fallback for unknown source/cookie modes.

The public contract stays small: persistence loads/saves `AppSettings`, while the settings view model maps between `AppSettings` and editable provider rows.

## Dependency Strategy

Dependency category: **Local-substitutable**.

The production implementation uses the local filesystem, but tests should inject a temp JSON path:

```csharp
var path = Path.Combine(tempDirectory, "settings.json");
var store = new FileAppSettingsStore(path);
```

No WinUI dependency belongs in the store. The view model can remain in the UI project if it uses WinUI types, but the mapping should avoid control types. If possible, keep editor state free of `FrameworkElement`, `Control`, and direct XAML references so it can be tested as ordinary .NET code.

Recommended dependency split:

- `FileAppSettingsStore` owns filesystem and serialization.
- A small normalization helper owns settings policy, either inside the store or as an internal settings normalizer.
- `SettingsViewModel` owns editable projection and conversion back to normalized `AppSettings`.
- `UsageMonitor` depends on `IAppSettingsStore`, not a concrete `SettingsStore`.

## Testing Strategy

New boundary tests to write:

- `LoadAsync_MissingFile_ReturnsDefaultSettings`
- `LoadAsync_EmptyOrInvalidJson_ReturnsDefaultSettings`
- `LoadAsync_InvalidRefreshMinutes_UsesDefaultRefreshMinutes`
- `LoadAsync_MissingProviderSettings_SeedsSupportedProviders`
- `SaveAsync_NormalizesProvidersBeforeWriting`
- `SaveAsync_WritesIndentedCompatibleJson`
- `SettingsViewModel_FromSettings_CreatesEditorsForSupportedProviders`
- `SettingsViewModel_ToSettings_ClampsRefreshMinutesAndTrimsCookieHeaders`
- `SettingsViewModel_ToSettings_ConvertsEmptyCookieHeaderToNull`
- `UsageMonitor_SaveSettingsAsync_AppliesSavedSettingsAndReconfiguresRefreshPolicy`

Old tests to delete or narrow after boundary tests exist:

- `AppSettingsTests.NormalizeProviders_*` can be reduced if normalization is no longer a public behavior callers are expected to invoke directly.
- `MainViewModelTests.ProviderSettingsEditorState_*` should move to settings-specific view model tests.
- Any future `SettingsPage` tests should avoid asserting mapping details that belong to `SettingsViewModel`.

Test environment needs:

- Temporary filesystem directories for `FileAppSettingsStore`.
- A fake `IAppSettingsStore` for `UsageMonitor` tests.
- No real `%LOCALAPPDATA%` dependency in tests.
- No WinUI window construction for settings mapping tests.

## Implementation Recommendations

The settings module should own:

- Loading and saving app settings.
- Default fallback behavior.
- Refresh interval validation.
- Provider map normalization.
- JSON compatibility for existing `settings.json` files.
- Editable projection between `AppSettings` and provider editor rows.

The module should hide:

- The physical settings path.
- Serialization options.
- Error handling for corrupt files.
- Which providers need default settings.
- How UI selection indexes map to enum values.
- Cookie header trimming/null conversion.

The module should expose:

- `IAppSettingsStore.LoadAsync()`.
- `IAppSettingsStore.SaveAsync(AppSettings settings)`.
- `SettingsViewModel.FromSettings(AppSettings settings)`.
- `SettingsViewModel.ToSettings()`.

Migration path:

1. Rename or replace `SettingsStore` with `FileAppSettingsStore` and inject the settings path through the constructor. Keep a production factory for the `%LOCALAPPDATA%\WinCodexBar\settings.json` path.
2. Add `IAppSettingsStore` and update `UsageMonitor` to depend on the interface.
3. Add `SettingsViewModel` and move `ProviderSettingsEditorState` plus settings conversion out of `SettingsPage.xaml.cs`.
4. Change `SettingsPage` to bind to `SettingsViewModel` and call `ViewModel.ToSettings()` on save.
5. Add store and view model boundary tests.
6. Delete or narrow tests that assert internal normalization mechanics once the new boundary tests cover observable behavior.

Keep this refactor separate from provider catalog redesign. The settings boundary can still use `ProviderCatalog.SupportedProviders` internally, but callers should stop needing to coordinate catalog defaults, editor rows, and persisted settings by hand.
