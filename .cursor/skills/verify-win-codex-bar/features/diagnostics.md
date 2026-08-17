# Diagnostics

Diagnostics shows a timeline of provider fetch attempts. The user filters by provider, copies the visible rows, opens the log folder, and expands a row's detail.

## Sub-features

- `diag-open` switches the main window from a provider panel to Diagnostics.
- `diag-filter` changes the filter combo (`All`, `Codex`, `Claude Code`, `Cursor`).
- `diag-copy` copies the visible rows with Copy diagnostics log.
- `diag-folder` exposes Open log folder when a log file path exists.
- `diag-detail` expands Show entry detail when a row has detail text.

## How to get to it (user POV)

- Choose `Diagnostics` in the provider selector.
- Choose a filter value, Copy, Open log folder, or a row's detail toggle.

## Driving it with control-wincodexbar

Preconditions:

- Win Codex Bar is healthy from `launch` + `doctor`.
- Wait until at least one refresh has run (Started/Completed or Attempt rows may already be present).

- **Open Diagnostics.** Choose Diagnostics. Run `control-wincodexbar.ps1 click -Name "Diagnostics"`. The heading `Diagnostics log` appears and the provider Retry button is gone.
- **Filter.** The combo id `DiagnosticsFilterComboBox` is present with `All` selected by default. Changing it keeps the table chrome (`Timestamp`, `Provider`, `Type`, `Source`, `Message`, `Duration`).
- **Copy.** Choose Copy diagnostics log. Run `control-wincodexbar.ps1 click -Id CopyDiagnosticsButton`. If rows exist, the clipboard contains timestamped lines; if none exist, the click is a no-op and that is acceptable.
- **Log folder.** Choose Open log folder only when the button is visible. Run `control-wincodexbar.ps1 click -Id OpenLogFolderButton`. Proof is that `%LOCALAPPDATA%\WinCodexBar\logs\diagnostics.log` exists, not an Explorer screenshot. Then close Explorer; do not treat Explorer as the app under test.
- **Proof.** Capture the Diagnostics panel. Run `control-wincodexbar.ps1 snapshot -Path .cursor/skills/verify-win-codex-bar/artifacts/diagnostics/panel.uia.txt` and `control-wincodexbar.ps1 screenshot -Path .cursor/skills/verify-win-codex-bar/artifacts/diagnostics/panel.png`. The artifacts show `Diagnostics log` and the filter combo.

## Gotchas

- The Diagnostics panel starts collapsed until `Diagnostics` is selected.
- Log contents may include fetch errors and redacted secrets. Do not paste log bodies into chat or into the skill files.
- Open log folder is hidden if the logger has no file path; report that as unreachable rather than clicking a missing button.
- Filtering does not change provider usage data; it only filters the table.
