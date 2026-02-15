# Win Codex Bar

Win Codex Bar is a WinUI 3 desktop tray app for monitoring Codex and Claude Code usage. It shows usage windows, reset timelines, and diagnostics from OAuth, web-cookie, or CLI sources.

## Features
- Tray + window UI for Codex and Claude usage snapshots.
- Configurable source mode per provider: `Auto`, `OAuth`, `Web (Cookies)`, or `CLI`.
- Diagnostics timeline with provider filtering.
- Configurable refresh interval and per-provider enable/disable settings.

## Project Layout
- `tray-ui/` WinUI 3 app (views, view models, services, manifests, assets).
- `tray-ui.Core/` shared models and provider fetch services.
- `tray-ui.Tests/` unit tests.

## Requirements
- Windows 10 1809+ (`10.0.17763.0` minimum).
- .NET 10 SDK.
- Visual Studio 2022+ with WinUI/Windows App SDK tooling (recommended for local UI run/debug).

## Build and Test
```powershell
dotnet restore tray-ui/tray-ui.csproj
dotnet build tray-ui/tray-ui.csproj -r win-x64
dotnet test tray-ui.Tests/tray-ui.Tests.csproj
```

## Run Locally
Open `tray-ui/tray-ui.slnx` (or `tray-ui/tray-ui.csproj`) in Visual Studio and run the app.

## Provider Setup
### Codex
- OAuth auth is read from `%CODEX_HOME%\auth.json` (if `CODEX_HOME` is set), otherwise `%USERPROFILE%\.codex\auth.json`.
- `Auto` source tries `OAuth`, then `Web`, then `CLI`.
- `CLI` source requires the `codex` command on `PATH`.

### Claude Code
- OAuth auth is read from `%USERPROFILE%\.claude\.credentials.json`.
- `Auto` source tries `OAuth`, then `Web`, then `CLI`.
- `CLI` source requires the `claude` command on `PATH`.

### Web Cookie Mode
- In Settings, choose source `Web (Cookies)` and set cookie source to `Manual`.
- Paste the full cookie header string for the provider.

## Settings and Packaging
- Runtime settings are stored in `ApplicationData.Current.LocalFolder/settings.json`.
- Packaging/config files: `tray-ui/Package.appxmanifest` and `tray-ui/app.manifest`.
- Publish profiles: `tray-ui/Properties/PublishProfiles/`.

## Security Note
- Do not commit provider cookies or auth tokens.
