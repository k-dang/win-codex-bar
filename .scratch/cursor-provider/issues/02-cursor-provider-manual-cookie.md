Status: done

# Cursor provider end-to-end via manual cookie (incl. legacy request plans)

## Parent

`.scratch/cursor-provider/PRD.md`

## What to build

The first complete, demoable path for Cursor as a third provider — using a manually pasted cookie header, so this slice carries no SQLite/JWT complexity and de-risks the HTTP + mapping layer on its own.

End-to-end behavior: register Cursor in the provider system (new `ProviderKind.Cursor`, catalog definition with `DisplayName = "Cursor"`, primary/secondary labels **"Total"/"Auto"**, cookie support, and default **disabled**). Add a manual-cookie `Web` usage source that, given the user's pasted Cursor cookie header, calls Cursor's API and produces a usage snapshot that renders as two bars with a monthly billing-cycle reset — just like Codex/Claude.

API calls (base `https://cursor.com`, `Cookie` + `Accept: application/json` headers):
- `GET /api/usage-summary` — primary payload. `401`/`403` surfaces as a "not logged in to Cursor" error via the existing provider error-snapshot path; other non-200 is a generic network error.
- `GET /api/auth/me` — best-effort; supplies account `sub` (and email/name). Failure does not fail the refresh.
- `GET /api/usage?user=<sub>` — best-effort legacy request-count endpoint; failures ignored.

Mapping is a pure, unit-tested `CursorUsageMapper` (money fields are in **cents**, timestamps ISO-8601):
- **Primary "Total" percent** precedence: `individualUsage.plan.totalPercentUsed` → average of `autoPercentUsed` + `apiPercentUsed` → either lane alone → `plan.used/plan.limit` → `individualUsage.overall` ratio → `teamUsage.pooled` ratio → `0`; clamp `[0,100]`.
- **Secondary "Auto" percent** = `plan.autoPercentUsed`; clamp `[0,100]`.
- **Window/reset:** both windows use the billing cycle — `WindowMinutes = (billingCycleEnd − billingCycleStart)` in minutes, `ResetsAt = billingCycleEnd`, reset text via the existing `UsageWindowFormatter`.
- **Legacy request plans (folded in):** when the `/api/usage` response carries a request limit (`maxRequestUsage` > 0), Primary becomes `requestsUsed / requestsLimit` percent and the Secondary "Auto" bar is omitted.

The source registers in the provider source factory and is consumed by the existing refresh pipeline unchanged. Only the two-window `UsageWindow` model is used — the API third bar and on-demand USD are out of scope (see PRD).

## Acceptance criteria

- [x] `ProviderKind.Cursor` exists and Cursor appears in `ProviderCatalog.SupportedProviders` with `DisplayName = "Cursor"`, `"Total"`/`"Auto"` labels, cookie support, and default-disabled.
- [x] With a valid manual cookie header configured, a refresh yields a Cursor snapshot whose Primary ("Total") and Secondary ("Auto") percentages and `ResetsAt` (billing-cycle end) are correct, and it renders as two labeled bars in the tray/main views. *(Verified at the unit level and via the generic catalog-driven rendering path; live end-to-end with a real cookie still needs a manual pass — paste a cursor.com cookie header in Settings → Cursor Provider with cookie source "Manual".)*
- [x] A `401`/`403` from `/api/usage-summary` produces the provider's "not logged in to Cursor" error state rather than an exception; other non-200 produces a network error.
- [x] `/api/auth/me` and `/api/usage` failures are swallowed and do not fail the overall Cursor refresh.
- [x] On a legacy request-based plan, Primary reflects requests used/limit and the "Auto" bar is hidden.
- [x] `CursorUsageMapper` is unit-tested across: `totalPercentUsed` present; auto+api averaging; plan used/limit ratio; `overall` and `pooled` fallbacks; clamping of out-of-range/fractional percents; missing billing dates (null window); and legacy request-plan projection.
- [x] `ProviderCatalogTests` and `AppSettingsTests` cover Cursor's presence, labels, and default-disabled state.
- [x] Existing pipeline tests still pass with Cursor registered (Cursor is a plain `IProviderUsageSource`, disabled by default so it is skipped unless enabled).
- [x] `dotnet test WinCodexBar.Tests/WinCodexBar.Tests.csproj` passes (72 tests).

## Blocked by

- `.scratch/cursor-provider/issues/01-per-provider-default-enabled.md`
