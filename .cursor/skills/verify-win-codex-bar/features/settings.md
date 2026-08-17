# Settings

Settings lets a user change the refresh interval and per-provider enable/source options, save them to disk, or cancel without writing.

## Sub-features

- `settings-open` opens Settings from the main window gear button.
- `settings-refresh` shows Refresh Interval (minutes) with id `RefreshMinutesNumberBox`.
- `settings-enable` shows Enable checkboxes for Codex, Claude Code, and Cursor.
- `settings-source` shows source combos labeled Auto, OAuth, Web (Cookies), and CLI (CLI omitted for Cursor).
- `settings-save` writes `%LOCALAPPDATA%\WinCodexBar\settings.json` and closes Settings.
- `settings-cancel` closes Settings without keeping unsaved edits.

## How to get to it (user POV)

- Choose the Settings gear on the main window (name `Settings`).
- Choose `Save` or `Cancel` in the Settings window.

## Driving it with control-wincodexbar

Preconditions:

- Win Codex Bar is healthy from `launch` + `doctor`.
- Launch has already copied `settings.json` to `.run/settings.json.bak` when that file existed.
- Do not Save unless the recipe will be followed by `cleanup` (restore).

- **Open Settings.** Choose Settings. Run `control-wincodexbar.ps1 click -Id SettingsButton` then `control-wincodexbar.ps1 wait -Id SaveSettingsButton -Window settings`. The Settings window heading reads `Settings`.
- **Refresh control.** Confirm the interval box. Run `control-wincodexbar.ps1 snapshot -Path .cursor/skills/verify-win-codex-bar/artifacts/settings/open.uia.txt -Window settings`. The tree contains id `RefreshMinutesNumberBox` and `SaveSettingsButton`.
- **Enable checkboxes.** The same snapshot contains names `Enable Codex usage`, `Enable Claude Code usage`, and `Enable Cursor usage`.
- **Cancel.** Choose Cancel. Run `control-wincodexbar.ps1 click -Id CancelSettingsButton -Window settings`. The Settings window closes. `settings.json` matches the launch backup.
- **Save (optional mutation).** Reopen Settings, change Refresh Interval only if you will assert the file then cleanup. Run `control-wincodexbar.ps1 click -Id SaveSettingsButton -Window settings`. `%LOCALAPPDATA%\WinCodexBar\settings.json` contains `"RefreshMinutes"` matching the control. Then run `cleanup` so the backup is restored.
- **Proof.** Capture the open Settings window before Cancel. Run `control-wincodexbar.ps1 screenshot -Path .cursor/skills/verify-win-codex-bar/artifacts/settings/open.png -Window settings`. The screenshot shows the `Settings` heading and Save/Cancel.

## Gotchas

- Save writes the user's real settings file. Cleanup restores the launch backup; skipping cleanup leaves the machine changed.
- Cookie header boxes can contain secrets. Do not snapshot their values into artifacts. Do not paste cookies into the skill files.
- Cursor has no CLI source. Assert `Auto`, `OAuth`, and `Web (Cookies)` only for that block.
- Settings is a second window (`-Window settings`). Clicks without that flag target the main window and miss Save/Cancel.
