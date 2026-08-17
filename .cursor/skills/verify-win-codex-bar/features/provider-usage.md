# Provider usage

The main window shows a live usage snapshot for each enabled provider. The user switches Codex, Claude Code, and Cursor, sees percent bars or an empty/error state, and can retry a refresh.

## Sub-features

- `usage-open` shows the main window with the provider selector and Settings.
- `usage-codex` selects Codex and shows that provider's panel.
- `usage-claude` selects Claude Code and shows that provider's panel.
- `usage-cursor` selects Cursor and shows that provider's panel.
- `usage-retry` invokes Retry on the selected provider.
- `usage-empty-or-metrics` shows either `No provider data` / an error string, or percent text for the selected provider.

## How to get to it (user POV)

- Launch the app; the main window opens on Codex.
- Choose `Codex`, `Claude Code`, or `Cursor` in the provider selector.
- Choose `Retry` on the selected provider panel.

## Driving it with control-wincodexbar

Preconditions:

- Win Codex Bar is healthy from `launch` + `doctor`.
- The main window is visible (`show` if it was hidden).

- **Open main.** Confirm the window is up. Run `control-wincodexbar.ps1 show` then `control-wincodexbar.ps1 snapshot -Path .cursor/skills/verify-win-codex-bar/artifacts/provider-usage/main.uia.txt`. The tree contains name `Win Codex Bar` or id `SettingsButton`, and names `Codex`, `Claude Code`, `Cursor`, `Diagnostics`.
- **Codex panel.** Choose Codex. Run `control-wincodexbar.ps1 click -Name "Codex"`. The heading `Codex` is visible and Diagnostics chrome (`Copy diagnostics log`) is not.
- **Claude panel.** Choose Claude Code. Run `control-wincodexbar.ps1 click -Name "Claude Code"`. The heading `Claude Code` is visible.
- **Cursor panel.** Choose Cursor. Run `control-wincodexbar.ps1 click -Name "Cursor"`. The heading `Cursor` is visible.
- **Retry.** Choose Retry. Run `control-wincodexbar.ps1 click -Id RetryProviderButton`. The Retry button is present (name `Retry provider refresh`). The panel still shows metrics, `No provider data`, or an error string afterward.
- **Proof.** Capture Codex selected. Run `control-wincodexbar.ps1 click -Name "Codex"`, `control-wincodexbar.ps1 snapshot -Path .cursor/skills/verify-win-codex-bar/artifacts/provider-usage/codex.uia.txt`, and `control-wincodexbar.ps1 screenshot -Path .cursor/skills/verify-win-codex-bar/artifacts/provider-usage/codex.png`. The screenshot shows the `Win Codex Bar` heading and the Codex panel.

## Gotchas

- Closing the window hides it; it does not exit. `show` before clicking if doctor finds the process but clicks miss.
- Usage percents require the user's OAuth/cookie/CLI sources. Do not fail the feature because the panel is empty or shows an auth error.
- `Diagnostics` replaces the provider panel. Reselect `Codex` before asserting usage chrome.
- Selector item names are `Claude Code`, not `Claude`.
