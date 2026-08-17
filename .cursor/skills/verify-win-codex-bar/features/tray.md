# Tray

The app stays in the notification area. Closing the main window hides it; double-clicking the tray icon or choosing Open on the tray menu restores it; Exit stops the process.

## Sub-features

- `tray-hide` hides the main window via the window close button without ending the process.
- `tray-show` restores the main window with `show` (same outcome as tray Open / double-click).
- `tray-exit` stops the tracked process via cleanup or the tray Exit button.

## How to get to it (user POV)

- Choose the window close button (hides to tray).
- Double-click the tray icon, or right-click the tray icon and choose `Open`.
- Right-click the tray icon and choose `Exit`.

## Driving it with control-wincodexbar

Preconditions:

- Win Codex Bar is healthy from `launch` + `doctor`.

- **Hide.** The close button is the WinUI caption close control (not an AutomationId in the page). After hide, `doctor` still reports the tracked pid. Clicks against a hidden window fail until `show`.
- **Restore.** Run `control-wincodexbar.ps1 show`. The main window is visible again with `SettingsButton`.
- **Tray menu Open.** The tray menu window title is `Tray Menu`. Buttons are name `Open` / id `OpenTrayMenuButton` and name `Exit` / id `ExitTrayMenuButton`. The menu is only present after a tray right-click, which this helper does not synthesize. Prefer `show` for restore proof unless you can open the menu from the tray icon in this session.
- **Exit.** Prefer `control-wincodexbar.ps1 cleanup`, which stops the tracked pid. If the tray menu is open, `control-wincodexbar.ps1 click -Id ExitTrayMenuButton -Window tray` also exits. After either path, `Get-Process -Name WinCodexBar` returns nothing for that pid.
- **Proof.** After `show`, run `control-wincodexbar.ps1 screenshot -Path .cursor/skills/verify-win-codex-bar/artifacts/tray/restored.png` and `control-wincodexbar.ps1 snapshot -Path .cursor/skills/verify-win-codex-bar/artifacts/tray/restored.uia.txt`. The artifacts show the main window identity. Process still running is the hide/restore proof; process gone is the Exit proof.

## Gotchas

- Never `Stop-Process -Name WinCodexBar`. That can kill a session this run did not start. Cleanup uses the tracked pid only.
- The tray menu is a separate window and is easy to miss with `-Window main`.
- Right-clicking the tray icon is not scripted in `control-wincodexbar.ps1`. If the menu cannot be opened, report tray-menu click paths as unreachable and prove hide/restore via close + `show`, and Exit via `cleanup`.
