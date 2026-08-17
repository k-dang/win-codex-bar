---
name: verify-win-codex-bar
description: Drive the Win Codex Bar WinUI tray app through Windows UI Automation. Use when proving usage snapshots, settings, diagnostics, or tray behavior the way a user would — not via unit tests or internal setters.
---

# Verify Win Codex Bar

Win Codex Bar is a packaged WinUI 3 desktop tray app. Verification launches it with `BuildAndRun.ps1`, drives windows through UI Automation, and captures a UIA tree plus a screenshot. There is no browser, CLI, or HTTP surface.

Helpers live in this skill. Invoke them from the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 <command>
```

Read `features/README.md` before driving. Use the matching feature file as the recipe.

## Launch

Preconditions: Windows Developer Mode on, .NET 10 SDK, `winapp` on PATH (`winget install Microsoft.WinAppCLI`). Do not run `WinCodexBar.exe` directly.

```powershell
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 launch
```

That runs `./BuildAndRun.ps1 -Detach`, records the new `WinCodexBar` pid in `.cursor/skills/verify-win-codex-bar/.run/state.json`, backs up `%LOCALAPPDATA%\WinCodexBar\settings.json` if it exists, and waits until the main window exposes `SettingsButton`.

Ready: the helper prints `Launched WinCodexBar pid=...` and `doctor` reports `healthy=true`.

Isolation: settings and logs live in `%LOCALAPPDATA%\WinCodexBar` with no per-run data directory. Two instances share that state and the packaged identity. **If any `WinCodexBar` process is already running, launch refuses.** Never attach to the user's session.

## Doctor

```powershell
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 doctor
```

Pass only when all of these hold:

- The tracked pid is a live `WinCodexBar` process.
- No other `WinCodexBar` process exists.
- The main window still has `AutomationId=SettingsButton`.

If anything looks off, run doctor before driving further.

## Drive

Show the main window if it was hidden to the tray (the close button hides; it does not exit):

```powershell
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 show
```

Click by AutomationId or accessible name. Target `-Window main` (default), `settings`, or `tray`.

```powershell
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 click -Id SettingsButton
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 click -Name "Diagnostics"
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 wait -Id SaveSettingsButton -Window settings
```

Stable handles from this app:

| Handle | Kind | Where |
|---|---|---|
| `SettingsButton` / name `Settings` | Button | Main window |
| name `Codex`, `Claude Code`, `Cursor`, `Diagnostics` | ListItem | Main window selector |
| `RetryProviderButton` / name `Retry provider refresh` | Button | Provider panel |
| `CopyDiagnosticsButton` / name `Copy diagnostics log` | Button | Diagnostics panel |
| `OpenLogFolderButton` / name `Open log folder` | Button | Diagnostics panel |
| `DiagnosticsFilterComboBox` | ComboBox | Diagnostics panel |
| name `Show entry detail` | ToggleButton | Diagnostics rows |
| `CancelSettingsButton`, `SaveSettingsButton` | Buttons | Settings window |
| `RefreshMinutesNumberBox` | NumberBox | Settings window |
| name `Enable Codex usage`, `Enable Claude Code usage`, `Enable Cursor usage` | CheckBoxes | Settings window |
| `OpenTrayMenuButton` / name `Open`, `ExitTrayMenuButton` / name `Exit` | Buttons | Tray menu window title `Tray Menu` |

Provider source combo boxes reuse `ProviderSourceComboBox` once per provider block; click the checkbox name first, then the combo in that block. Cookie fields (`CookieSourceComboBox`, `CookieHeaderTextBox`) appear when the source is Web.

Prefer these names and ids over coordinates. After each click, snapshot or screenshot before the next action.

## Evidence

Store proof under `.cursor/skills/verify-win-codex-bar/artifacts/<feature>/`. That folder is gitignored; it must still exist on disk after cleanup.

```powershell
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 snapshot -Path .cursor/skills/verify-win-codex-bar/artifacts/<feature>/window.uia.txt
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 screenshot -Path .cursor/skills/verify-win-codex-bar/artifacts/<feature>/window.png
```

Proof standards:

- Exercise the real window, not view-model setters or `dotnet test`.
- Capture the action and the resulting state (UIA tree plus screenshot with the window title chrome or the `Win Codex Bar` / `Settings` heading visible).
- For settings mutations, also read `%LOCALAPPDATA%\WinCodexBar\settings.json` after Save, then restore via cleanup.
- Usage numbers depend on the user's Codex/Claude/Cursor credentials. An empty state showing `No provider data` or a visible error string is valid proof that the panel rendered; do not invent usage percents.
- Copy-diagnostics proof is the clipboard plus the on-screen log rows, not a test-only dump.
- Opening the log folder launches Explorer; prove it by the folder path `%LOCALAPPDATA%\WinCodexBar\logs` existing and `diagnostics.log` growing after a refresh, not by screenshotting Explorer.

## Cleanup

```powershell
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 cleanup
```

Stops only the tracked pid (the close button would only hide the window). Restores `settings.json` from the launch backup. Deletes `.run/state.json`. Does **not** delete artifacts, does **not** delete `%LOCALAPPDATA%\WinCodexBar\logs`, and does **not** kill by process name.

If a drive fails, run cleanup before the next launch so the shared settings file and pid are not left dirty.

## Helpers

`scripts/control-wincodexbar.ps1` commands: `launch`, `doctor`, `show`, `click`, `wait`, `snapshot`, `screenshot`, `cleanup`. Flags: `-Id`, `-Name`, `-Path`, `-Window main|settings|tray`, `-TimeoutSeconds`.
