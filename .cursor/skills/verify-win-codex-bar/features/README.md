# Win Codex Bar verification map

This directory is the maintained source for verifying the user-facing behavior of Win Codex Bar. Read the index before driving the app, then use the matching feature file as the recipe.

## Baseline preconditions

- No `WinCodexBar` process is running.
- Developer Mode is enabled. `winapp` is on PATH.
- Launch with `scripts/control-wincodexbar.ps1 launch` from the repo root.
- Run `scripts/control-wincodexbar.ps1 doctor` and require `healthy=true` and the tracked pid.
- Never drive an instance that was not started by this verification run.
- Settings live in `%LOCALAPPDATA%\WinCodexBar\settings.json`. Launch backs that file up; cleanup restores it.

## Driving conventions

- Start every recipe from the main window unless its preconditions say otherwise. Closing the window hides it to the tray; `show` restores it. `Exit` on the tray menu (or `cleanup`) is what stops the process.
- Prefer AutomationId and accessible names over coordinates.
- Treat every command as literal. Keep quoted names and flags unchanged.
- Run all UI actions through `control-wincodexbar.ps1`.
- Restore settings via cleanup after any Settings Save. Do not remove proof artifacts during cleanup.

## Proof and skip reporting

- Capture the user action and the resulting state, not only the final screen.
- UI proof includes a UIA snapshot and a screenshot with the app identity visible (`Win Codex Bar` heading or `Settings` heading).
- Settings mutation proof includes the saved `settings.json` values.
- Record the feature ID and entry point used with every artifact.
- Report an unreachable path with the attempted command and the unmet precondition.
- Do not report a skipped entry point as verified through a different path.
- Empty or error usage panels are acceptable when credentials are missing. Missing chrome (selector, Settings, Diagnostics) is not.

## Feature entry contract

Each feature file starts with an H1 title and one paragraph describing the user-visible behavior. It then uses exactly four H2 sections in this order.

1. `Sub-features` lists short IDs with one line for each behavior.
2. `How to get to it (user POV)` lists every user entry point.
3. `Driving it with control-wincodexbar` starts with `Preconditions:` and uses labeled bullets that pair each user action with an exact command and observable result.
4. `Gotchas` lists traps that can waste or invalidate a verification run.

Keep implementation details out of the map. Name only user paths, stable handles, required state, commands, and observable proof.

## Features

- [Provider usage](./provider-usage.md) covers the Codex, Claude Code, and Cursor panels, empty/error states, and Retry.
- [Settings](./settings.md) covers opening Settings, refresh interval, provider enable, source mode, Save, and Cancel.
- [Diagnostics](./diagnostics.md) covers the Diagnostics tab, filter, copy, log folder, and entry detail.
- [Tray](./tray.md) covers hide-to-tray, restore, and Exit.
