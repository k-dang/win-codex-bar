Status: done

# Per-provider default-enabled (prefactor)

## Parent

`.scratch/cursor-provider/PRD.md`

## What to build

A prefactor that lets each provider declare whether it is enabled by default, instead of every provider defaulting to enabled. Today `ProviderSettings.CreateDefault` hardcodes `Enabled = true` for all providers; a new provider (Cursor) needs to ship disabled. Thread a per-provider "default enabled" value from the provider definition into default-settings creation, so `CreateDefault` reads the provider's own default rather than a constant.

This slice introduces no user-visible behavior change: the two existing providers (Codex, Claude) keep defaulting to enabled. It exists to make the Cursor slice a trivial addition ("make the change easy, then make the easy change").

## Acceptance criteria

- [x] `ProviderDefinition` exposes a per-provider default-enabled value; Codex and Claude both resolve to enabled.
- [x] `ProviderSettings.CreateDefault` derives `Enabled` from the provider's definition rather than a hardcoded `true`.
- [x] `AppSettings.CreateDefault` / `NormalizeProviders` produce the same enabled state as before for existing providers (no regression).
- [x] The fallback definition path (unknown providers) still yields a sensible default.
- [x] `ProviderCatalogTests` / `AppSettingsTests` cover that a provider marked default-disabled produces `Enabled = false` while existing providers stay enabled.
- [x] `dotnet test WinCodexBar.Tests/WinCodexBar.Tests.csproj` passes.

## Blocked by

- None - can start immediately
