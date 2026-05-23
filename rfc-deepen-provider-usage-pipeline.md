## Problem

The provider usage pipeline is currently split across shallow modules that only make sense together:

- `ProviderUsageService` owns provider ordering, disabled-provider filtering, refresh lifecycle logging, missing-fetcher errors, null-result normalization, and exception-to-snapshot conversion.
- `CodexProviderUsageFetcher` and `ClaudeProviderUsageFetcher` each duplicate source ordering, fallback, attempt timing, attempt diagnostics, error aggregation, and snapshot construction.
- Static helper classes own credential file reads, token refresh, HTTP request construction, CLI process execution, config lookup, JSON parsing, and reset-window mapping.
- `UsageMonitor` still owns single-provider summary replacement, even though that behavior is part of refresh semantics rather than UI state.

The result is a shallow public seam: `IProviderUsageFetcher` is easy to fake, but it bypasses the behavior most likely to break. Current tests can verify that `ProviderUsageService` dispatches to a fake fetcher, but they do not naturally exercise real source fallback, credential refresh and persistence, CLI fallback, diagnostics ordering, or final user-facing snapshot shaping.

This makes the code harder to navigate because understanding one refresh requires bouncing between the service, provider fetchers, static credential stores, static API clients, CLI helpers, process runner, formatter, and monitor merge code. It also makes the code harder to test at the right boundary: the interesting behavior lives between these modules, not inside any single shallow one.

## Proposed Interface

Create a deeper provider usage refresh pipeline with a small app-facing interface:

```csharp
public interface IUsageRefreshPipeline
{
    Task<UsageSummary> RefreshAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default);

    Task<UsageSummary> RefreshProviderAsync(
        AppSettings settings,
        UsageSummary currentSummary,
        ProviderKind provider,
        CancellationToken cancellationToken = default);
}
```

Usage from `UsageMonitor` becomes:

```csharp
public async Task RefreshAsync()
{
    Summary = await _usageRefreshPipeline.RefreshAsync(_settings);
    _dispatcherQueue.TryEnqueue(() => SummaryUpdated?.Invoke(this, Summary));
}

public async Task RefreshProviderAsync(ProviderKind provider)
{
    Summary = await _usageRefreshPipeline.RefreshProviderAsync(_settings, Summary, provider);
    _dispatcherQueue.TryEnqueue(() => SummaryUpdated?.Invoke(this, Summary));
}
```

Internally, the implementation should use provider/source adapters:

```csharp
internal interface IProviderUsageSource
{
    ProviderKind Provider { get; }
    ProviderSourceMode SourceMode { get; }

    Task<ProviderUsageSnapshot?> FetchAsync(
        ProviderUsageSourceRequest request,
        CancellationToken cancellationToken);
}

internal sealed record ProviderUsageSourceRequest(
    ProviderKind Provider,
    ProviderSettings ProviderSettings,
    AppSettings AppSettings);
```

Initial internal source adapters:

```csharp
internal sealed class CodexOAuthUsageSource : IProviderUsageSource;
internal sealed class CodexWebUsageSource : IProviderUsageSource;
internal sealed class CodexCliUsageSource : IProviderUsageSource;

internal sealed class ClaudeOAuthUsageSource : IProviderUsageSource;
internal sealed class ClaudeWebUsageSource : IProviderUsageSource;
internal sealed class ClaudeCliUsageSource : IProviderUsageSource;
```

The pipeline owns:

- Provider ordering and enabled-provider filtering.
- Full refresh and single-provider refresh semantics.
- Single-provider summary replacement.
- Source selection from `ProviderSettings.SourceMode`.
- Shared source fallback order.
- Missing-source, no-data, and exception-to-error-snapshot normalization.
- Refresh lifecycle diagnostics.
- Per-provider/source attempt diagnostics.
- Mapping source results into stable `ProviderUsageSnapshot` output.

Provider-specific source adapters own only provider-specific extraction details. They should not own the shared fallback algorithm.

## Dependency Strategy

This refactor is a hybrid of in-process deepening and ports/adapters for external effects.

- **In-process**: provider ordering, enabled filtering, source ordering, fallback policy, summary replacement, error normalization, snapshot shaping, cookie parsing, JSON payload mapping, and reset description formatting should live inside the deepened pipeline/module and be tested through `IUsageRefreshPipeline`.
- **Local-substitutable**: credential files and config files should move behind credential/config store ports. Production adapters read the real user profile and `CODEX_HOME`; tests use in-memory or temp-directory stores.
- **True external / Mock**: OpenAI/ChatGPT, Anthropic/Claude, and local `codex` / `claude` CLI processes are external dependencies. Mock them at HTTP and CLI runner ports rather than at `CodexProviderUsageFetcher` or `ClaudeProviderUsageFetcher`.
- **Diagnostics as port**: keep the current diagnostics logger behavior, but adapt it behind a pipeline diagnostics port so tests can assert deterministic attempt and refresh events.
- **Clock as port**: replace direct `DateTimeOffset.Now` and raw `Stopwatch` usage inside the pipeline with a clock/stopwatch port so `UpdatedAt`, reset descriptions, and durations are deterministic in boundary tests.

Suggested internal ports:

```csharp
internal interface IUsageHttpClient
{
    Task<UsageHttpResponse> SendAsync(
        UsageHttpRequest request,
        CancellationToken cancellationToken);
}

internal interface IProviderCredentialStore
{
    Task<CodexOAuthCredentials?> LoadCodexAsync(CancellationToken cancellationToken);
    Task SaveCodexAsync(CodexOAuthCredentials credentials, CancellationToken cancellationToken);
    Task<ClaudeOAuthCredentials?> LoadClaudeAsync(CancellationToken cancellationToken);
}

internal interface IProviderConfigStore
{
    Task<CodexProviderConfig> LoadCodexConfigAsync(CancellationToken cancellationToken);
}

internal interface IUsageCliRunner
{
    Task<string?> RunInteractiveAsync(
        string fileName,
        string arguments,
        string input,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<JsonElement?> RunJsonRpcAsync(
        string fileName,
        string arguments,
        IReadOnlyList<JsonRpcRequest> requests,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal interface IUsageClock
{
    DateTimeOffset Now { get; }
    IUsageStopwatch StartStopwatch();
}
```

Keep these ports internal unless there is a real external caller or plugin scenario. Tests can access internals through the existing test project pattern if needed.

## Testing Strategy

New boundary tests should exercise `IUsageRefreshPipeline` with fake ports and fake source adapters where appropriate:

- `RefreshAsync_ReturnsEnabledProvidersInStableOrder`
- `RefreshAsync_SkipsDisabledProviders`
- `RefreshAsync_WhenOAuthHasNoCredentials_FallsThroughToWebThenCli`
- `RefreshAsync_WhenSourceThrows_TriesNextSourceAndLogsFailure`
- `RefreshAsync_WhenAllSourcesFail_ReturnsSingleUserFacingErrorSnapshot`
- `RefreshAsync_WhenCodexCredentialsNeedRefresh_SavesRefreshedCredentials`
- `RefreshAsync_WhenCliFallbackSucceeds_ReturnsCliSnapshot`
- `RefreshProviderAsync_ReplacesOnlyTargetProviderInCurrentSummary`
- `RefreshProviderAsync_WhenProviderDisabled_PreservesCurrentSummary`
- `RefreshAsync_EmitsRefreshAndSourceDiagnosticsInOrder`
- `RefreshAsync_UsesInjectedClockForUpdatedAtAndResetDescriptions`

Old tests to delete or rewrite:

- Most `ProviderUsageServiceTests` that only assert dispatch to fake `IProviderUsageFetcher`.
- Provider fetcher tests that would only duplicate the shared fallback algorithm after it moves into the pipeline.
- Formatter-only tests for reset descriptions once reset text is tested through the pipeline boundary, unless the formatter remains a deliberately standalone utility.

Adapter-specific tests should remain narrow:

- File credential/config adapters parse real persisted shapes using temp directories.
- HTTP adapters build expected request method, URL, headers, and parse representative response payloads.
- CLI adapter parses representative `codex` and `claude` outputs without launching real processes.

## Implementation Recommendations

The module should own refresh behavior end to end. A caller should ask for a refreshed `UsageSummary`; it should not compose provider fetchers, merge partial summaries, decide source fallback, or translate exceptions into provider snapshots.

Keep the public interface small and app-shaped. Do not expose source override knobs, attempt records, or provider/source registration publicly until a real caller needs them. Diagnostics should continue through the existing UI-facing logger, but the pipeline should own when those events fire.

Migrate incrementally:

1. Introduce `IUsageRefreshPipeline` alongside the existing service.
2. Move full-refresh and single-provider summary replacement behavior into the pipeline.
3. Split current provider fetchers into internal source adapters.
4. Move duplicated source fallback and attempt diagnostics into the pipeline.
5. Replace static credential/config/CLI helpers with internal ports and production adapters.
6. Update `UsageMonitor` to depend on `IUsageRefreshPipeline`.
7. Rewrite `ProviderUsageServiceTests` into pipeline boundary tests, then remove the shallow `IProviderUsageFetcher` seam if no longer needed.

The durable direction is a deep module with one stable boundary: refresh settings into a summary. Everything else is implementation detail unless another real workflow proves otherwise.
