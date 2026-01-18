# Win Codex Bar Plan

## Goals
- Build a native Windows tray app (WinUI 3) that reads Codex/Claude JSONL logs and shows token usage.
- Keep the app lightweight with a background scanner, cache, and minimal UI.

## Milestones
1. Foundation
   - [ ] Create WinUI 3 app project.
   - [ ] Add tray icon support via Win32 interop (NotifyIcon).
   - [ ] Define settings model (log roots, refresh interval) + storage location.

2. Log pipeline
   - [ ] Implement JSONL scanner for Codex and Claude logs.
   - [ ] Normalize records into a unified usage model (daily totals + recent sessions).
   - [ ] Add per-file cache (mtime/size/offset + last parsed line) to avoid full rescans.

3. UI + tray workflow
   - [ ] Tray icon with context menu (Open, Refresh, Exit).
   - [ ] Lightweight window showing: today, last 7/30 days, and last session.
   - [ ] Settings window for log roots + refresh interval.

4. Refresh + resiliency
   - [ ] Manual refresh command wired to scanner.
   - [ ] Optional live refresh on file changes (FileSystemWatcher).
   - [ ] Handle log rotation and partial lines gracefully.

5. Validation
   - [ ] Manual test with sample JSONL logs for both providers.
   - [ ] Validate cache correctness and refresh timing.
   - [ ] Basic perf check with large log files.

## Specs
- Scanner core language: C# only for v1; keep architecture modular so a Rust parser can be added later if perf or parsing complexity demands it.
- Tray UI: show a compact inline tooltip summary (today + last session) and also allow opening the window for full details.
- Support additional providers beyond Codex/Claude now or later? Defer until after v1; keep the normalization layer extensible to add more providers later.
- Expected log locations on Windows (defaults): Codex `%USERPROFILE%\.codex\logs`; Claude `%APPDATA%\Claude\logs` (or `%LOCALAPPDATA%\Claude\logs` if the app uses local storage). Provide UI to add/override roots and scan `*.jsonl` to limit noise.
