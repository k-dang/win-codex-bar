Status: done

# Zero-config Cursor auth from the local Cursor.app token

## Parent

`.scratch/cursor-provider/PRD.md`

## What to build

The zero-config default authentication path for Cursor: derive a Cursor web session from the locally installed Cursor desktop app so the user doesn't have to paste a cookie. This builds on the manual-cookie slice, reusing its usage-summary fetch and `CursorUsageMapper`.

Behavior:
- Read the Cursor desktop app's SQLite state DB at `%APPDATA%\Cursor\User\globalStorage\state.vscdb`, `ItemTable` key `cursorAuth/accessToken`, opened **read-only** with a short busy timeout (the DB is large and held open while Cursor runs). This read lives behind an internal interface so the returned token can be faked in tests; the raw SQLite read itself is not unit-tested (consistent with the existing OAuth/CLI I/O adapters).
- Add the `Microsoft.Data.Sqlite` NuGet package to `WinCodexBar.Core` (its first external package).
- Derive the session from the token (pure, unit-tested logic):
  - Decode the JWT payload; `userID` = the `sub` claim's segment after the last `|` (e.g. `google-oauth2|user_01ABC` → `user_01ABC`); reject IDs containing anything outside alphanumerics and `._-`.
  - Treat the session as usable only when the token is non-empty, a valid `userID` is present, and `exp` is more than 60 seconds in the future.
  - Build the cookie header `WorkosCursorSessionToken=<userID>%3A%3A<accessToken>` (the `::` URL-encoded as `%3A%3A`).
- Register this as Cursor's **primary** source, ahead of the manual-cookie fallback, so the pipeline uses whichever credential works. No OAuth-style token refresh: an expired/missing local token is reported as unavailable, allowing fallback (manual cookie) or the provider's "not logged in" state.

Verifiable: with the Cursor desktop app installed and signed in, enabling Cursor shows usage with no cookie pasted.

## Acceptance criteria

- [x] `Microsoft.Data.Sqlite` is referenced by `WinCodexBar.Core` and the state DB is opened read-only without disturbing a running Cursor. *(Plus a direct `SQLitePCLRaw.bundle_e_sqlite3` 3.0.3 reference to replace the CVE-flagged transitive 2.1.11 native lib.)*
- [x] The local-token source is tried before the manual-cookie source; a usable local token yields a Cursor snapshot with no manual cookie configured. *(Registered in the `OAuth` slot, which Auto order tries before `Web`; verified live against cursor.com via a probe harness — real snapshot with no cookie configured.)*
- [x] `userID` extraction, malformed-ID rejection, the `exp > now + 60s` usability rule, empty-token handling, and the exact `WorkosCursorSessionToken=<userID>%3A%3A<token>` header are unit-tested via faked token values (`CursorAppSessionTests`).
- [x] An expired or missing local token is treated as unavailable and falls through to the manual-cookie source rather than throwing (`CursorAppTokenUsageSourceTests` also assert no HTTP request is made).
- [x] Manual-cookie behavior from the prior slice still works when no local token is available.
- [x] The Cursor session token is only read locally and sent solely to `https://cursor.com` over HTTPS.
- [x] `dotnet test WinCodexBar.Tests/WinCodexBar.Tests.csproj` passes (98 tests), and the app builds via the packaged WinUI path (`BuildAndRun.ps1 -SkipRun`).

## Blocked by

- `.scratch/cursor-provider/issues/02-cursor-provider-manual-cookie.md`
