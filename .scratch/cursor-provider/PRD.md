Status: ready-for-agent

# Add Cursor as a Third Tracked Provider

## Problem Statement

WinCodexBar tracks usage for Codex and Claude Code, but I also code in Cursor and have no visibility into my Cursor plan usage without opening the Cursor dashboard in a browser. I want Cursor to appear alongside Codex and Claude in the same tray/usage surface, ideally with zero manual setup since I'm already logged into the Cursor desktop app on this machine.

## Solution

Add Cursor as a first-class provider in the existing provider pipeline. When enabled, the app reads the locally installed Cursor desktop app's session token, calls Cursor's usage API, and shows Cursor's plan usage as two bars ("Total" and "Auto") with a monthly billing-cycle reset — exactly the way Codex and Claude usage already render. If the local app token isn't available, the user can paste a cookie header manually. Cursor ships disabled by default so existing users see no change until they opt in.

## User Stories

1. As a Cursor user, I want Cursor usage tracked next to Codex and Claude, so that I have one place to see all my AI coding quota.
2. As a Cursor user already signed into the Cursor desktop app, I want the app to pick up my session automatically, so that I don't have to configure anything.
3. As a Cursor user, I want a "Total" usage bar, so that I can see how much of my included plan I've consumed this billing cycle.
4. As a Cursor user, I want an "Auto" usage bar, so that I can see my Auto/Composer consumption separately from the headline number.
5. As a Cursor user, I want the reset time to reflect my monthly billing cycle end, so that I know when my usage resets.
6. As a user who doesn't use Cursor, I want Cursor tracking off by default, so that my existing view is unchanged and no failed Cursor calls appear.
7. As a Cursor user, I want to enable/disable Cursor from settings just like the other providers, so that the experience is consistent.
8. As a Cursor user without the desktop app installed, I want to paste a Cursor cookie header manually, so that I can still track usage.
9. As a Cursor user, I want a clear, provider-specific error message when my session is missing or rejected, so that I know I need to log in to Cursor again.
10. As a Cursor user, I want the app to transparently fall back from the local token to a manual cookie header, so that whichever credential works is used.
11. As a Cursor user on an expired local token, I want the app to treat it as unavailable rather than sending a doomed request, so that I get a meaningful "logged out" state.
12. As a Cursor user, I want my Cursor row to refresh on the same schedule and manual-refresh actions as the other providers, so that all data stays current together.
13. As a Cursor user on a legacy request-based plan, I want my request quota (used/limit) surfaced as the primary bar, so that the number reflects how my plan actually meters usage.
14. As a maintainer, I want Cursor to plug into the existing provider catalog and source factory, so that no bespoke rendering or refresh path is introduced.
15. As a maintainer, I want the Cursor usage-summary parsing to be a pure, unit-tested function, so that the percentage/reset mapping is verified without network or SQLite.
16. As a maintainer, I want the local-token-to-cookie derivation unit-tested, so that userID extraction and expiry handling are correct across token shapes.
17. As a maintainer, I want Cursor's provider definition and settings normalization covered by the existing catalog/settings tests, so that the new enum value is wired correctly end to end.
18. As a privacy-conscious user, I want my Cursor session token only read locally and sent solely to Cursor's own API over HTTPS, so that my credential isn't exposed elsewhere.

## Implementation Decisions

### Provider identity and catalog
- Add `ProviderKind.Cursor = 3` to the provider enum.
- Add a `ProviderDefinition` for Cursor to `ProviderCatalog.SupportedProviders`:
  - `DisplayName = "Cursor"`, usage/settings/enabled/source labels following the Codex/Claude pattern.
  - `PrimaryUsageLabel = "Total"`, `SecondaryUsageLabel = "Auto"` (relabels the two existing window slots; no model change).
  - `SupportedSourceModes` limited to the modes Cursor actually supports (Auto + the local-token source + Web/manual cookie). Browser-cookie import is **not** included (macOS-only in the reference).
  - `SupportsCookieHeader = true` with Cursor-specific cookie labels/placeholder, reusing the existing manual-cookie mechanism.
- Because `AppSettings.CreateDefault`/`NormalizeProviders` iterate `ProviderCatalog.SupportedProviderKinds`, Cursor gets settings wiring automatically. Cursor must default to **disabled** (`Enabled = false`) — this differs from the current catalog providers, so provider default settings must support a per-provider "default enabled" value rather than the current always-`true` default.

### Authentication (Windows)
- **Primary source — local Cursor.app token (zero-config):**
  - Read the Cursor desktop app's SQLite state DB at `%APPDATA%\Cursor\User\globalStorage\state.vscdb`, `ItemTable` key `cursorAuth/accessToken`, opened read-only with a short busy timeout (the DB is large and held open by a running Cursor).
  - This is a thin I/O adapter behind an internal interface so the token value can be faked in tests; the raw read itself is not unit-tested (consistent with existing OAuth/CLI adapters).
  - Derive the web session from the token (pure logic, unit-tested):
    - Decode the JWT payload; `userID` = the `sub` claim's segment after the last `|` (e.g. `google-oauth2|user_01ABC` → `user_01ABC`); validate it contains only alphanumerics and `._-`.
    - Consider the session usable only when the token is non-empty, a valid `userID` is present, and `exp` is more than 60 seconds in the future.
    - Build the cookie header: `WorkosCursorSessionToken=<userID>%3A%3A<accessToken>` (the `::` is URL-encoded as `%3A%3A`).
- **Fallback source — manual cookie header:** reuse the existing `ProviderSettings.CookieHeader` + `CookieSourceMode.Manual` path (as Codex/Claude Web sources do). When a manual header is present it is used directly.
- The two sources map onto the existing `ProviderSourceMode` slots and register in `ProviderUsageSourceFactory.CreateDefault`. The pipeline's existing source-order/fallback/error-snapshot behavior is reused unchanged. No OAuth token-refresh flow is implemented (unlike Codex); an expired local token is simply reported as unavailable.

### API contract
- Base URL `https://cursor.com`; all requests send the derived/manual `Cookie` header and `Accept: application/json`.
- `GET /api/usage-summary` — primary payload. `401`/`403` ⇒ a "not logged in to Cursor" error (surfaced via the existing provider error-snapshot path). Non-200 ⇒ generic network error.
- `GET /api/auth/me` — best-effort; supplies account email/name and `sub`. Failure does not fail the refresh.
- `GET /api/usage?user=<sub>` — best-effort legacy request-count endpoint; wrapped so failures are ignored. `sub` comes from `/api/auth/me` or, as a fallback, the `userID` derived from the local token.

### Usage-summary → window mapping (pure function `CursorUsageMapper`)
- Values in `usage-summary` money fields are in **cents**; billing-cycle timestamps are ISO-8601.
- **Primary "Total" percent** resolves by this precedence: `individualUsage.plan.totalPercentUsed` → average of `autoPercentUsed` + `apiPercentUsed` → either lane alone → `plan.used / plan.limit` ratio → `individualUsage.overall` ratio → `teamUsage.pooled` ratio → `0`. Clamp to `[0, 100]`.
- **Secondary "Auto" percent** = `individualUsage.plan.autoPercentUsed`, clamped to `[0, 100]`.
- **Window/reset:** both windows use the monthly billing cycle — `WindowMinutes = (billingCycleEnd − billingCycleStart)` in minutes, `ResetsAt = billingCycleEnd`, with the reset description formatted via the existing `UsageWindowFormatter`.
- **Legacy request plans:** when the legacy `/api/usage` response carries a `maxRequestUsage` (request limit) > 0, the Primary bar becomes `requestsUsed / requestsLimit` percent and the Secondary "Auto" bar is hidden (the token-based Auto/API percentages are meaningless against a request quota).
- **v1 scope trims (see Out of Scope):** the reference's third "API" bar and the on-demand USD cost figure are **not** surfaced; only Primary + Secondary windows are produced, matching the existing two-window `UsageWindow` model.

### New dependency
- Add the `Microsoft.Data.Sqlite` NuGet package to `WinCodexBar.Core` (its first external package) for the read-only state DB access.

## Testing Decisions

Good tests here exercise externally observable behavior — the percentage/reset numbers produced from a given API payload, and the cookie/userID produced from a given token — not private wiring or live network/SQLite. Follow the existing `MethodName_Scenario_ExpectedResult` naming.

- **`CursorUsageMapper` (new, highest-value unit):** feed representative parsed `usage-summary` payloads and assert the resulting Primary/Secondary `UsageWindow` percentages, `WindowMinutes`, and `ResetsAt`. Cover: `totalPercentUsed` present; auto+api averaging fallback; plan used/limit ratio; `overall` and `pooled` fallbacks; clamping of out-of-range/fractional percents; missing billing dates (null window); and the legacy request-plan projection (primary = requests ratio, secondary hidden). Prior art: reference `CursorStatusProbeTests`, `CursorEnterpriseUsageTests`, `CursorLegacyRequestProjectionTests`; local analogues are the internal mappers exercised indirectly today.
- **Local-token cookie derivation (new unit):** assert `userID` extraction from varied `sub` shapes, charset rejection of malformed IDs, the "usable only when `exp` > now+60s and token non-empty" rule, and the exact `WorkosCursorSessionToken=<userID>%3A%3A<token>` header. Prior art: reference `CursorAppAuthSession` behavior.
- **`ProviderCatalog` (extend `ProviderCatalogTests`):** Cursor definition present with `DisplayName = "Cursor"`, `"Total"`/`"Auto"` labels, expected supported source modes, and cookie support.
- **`AppSettings` (extend `AppSettingsTests`):** normalization creates Cursor settings, and Cursor's default is **disabled** while Codex/Claude remain enabled.
- **Pipeline behavior:** no new tests — Cursor sources are plain `IProviderUsageSource` instances already covered generically by `UsageRefreshPipelineTests` (source ordering, fallback-on-null, error snapshot, disabled-provider skip) via `FakeSource`.
- The SQLite read adapter and live HTTP calls are verified manually in the running app (not unit-tested), matching how the existing OAuth/CLI I/O adapters are handled.

## Out of Scope

- The third "API" (named-model) usage bar and any tertiary window (the shared `UsageWindow` model stays two-window).
- On-demand / extra usage USD cost display (no cost/currency concept is added to the model in v1).
- Browser cookie import from Chrome/Firefox/Safari (macOS-only in the reference; Windows relies on the local app token + manual cookie).
- Team/enterprise pooled and personal-cap USD breakdowns beyond their use as a Primary-percent fallback.
- OAuth-style automatic token refresh for Cursor (expired local token is reported as unavailable rather than refreshed).
- An in-app Cursor login/"add account" flow.
- Membership-type/account-identity display (email, plan name) as distinct UI — the current model surfaces windows only.

## Further Notes

- Reference implementation lives at `opensrc/repos/github.com/steipete/CodexBar` — the Cursor provider under `Sources/CodexBarCore/Providers/Cursor/` (`CursorStatusProbe.swift`, `CursorRequestUsage.swift`, `CursorProviderDescriptor.swift`). The macOS app leads with browser session cookies and treats the local app token as a last-resort fallback; on Windows we invert that priority because the local `state.vscdb` token is the reliable, zero-config source.
- The local token confirmed on this machine is a JWT with `iss=https://authentication.cursor.sh`, `aud=https://cursor.com`, `sub=google-oauth2|…`, and an `exp` claim — matching the reference's parsing assumptions.
- The "API is cookie-only" detail is important: even the local JWT is sent as the value half of the `WorkosCursorSessionToken` cookie, never as an `Authorization: Bearer` header.
- If richer data (API bar, on-demand USD, membership label) is wanted later, it would require extending the `UsageWindow`/snapshot model and the tray/settings views — deferred out of this PRD.
