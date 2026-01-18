# Repository Guidelines

## Project Structure & Module Organization
The WinUI 3 desktop app lives under `tray-ui/`. Core entry points are `tray-ui/App.xaml`, `tray-ui/App.xaml.cs`, and `tray-ui/MainWindow.xaml` for the shell window. UI pages are in `tray-ui/Views/` (for example, `AllNotesPage.xaml` and `NotePage.xaml`), and simple data models live in `tray-ui/Models/`. App assets and icons are stored in `tray-ui/Assets/`. Packaging and app configuration are in `tray-ui/Package.appxmanifest` and `tray-ui/app.manifest`, while publish profiles and launch settings live in `tray-ui/Properties/`. Build outputs are under `tray-ui/bin/` and `tray-ui/obj/` and should not be edited manually.

## Build, Test, and Development Commands
- `dotnet build tray-ui/tray-ui.csproj -r win-x64` builds the WinUI app.
- `dotnet test` runs unit tests in the project.

## Coding Style & Naming Conventions
Use 4-space indentation in both C# and XAML. Follow standard .NET naming: PascalCase for classes, methods, and properties; camelCase for local variables and private fields. Page files should use the `*Page.xaml` suffix, and `x:Name` should be added only for elements referenced in code-behind. Nullable reference types are enabled in `tray-ui/tray-ui.csproj`, so address warnings rather than suppressing them.

## Testing Guidelines
There is no dedicated test project in this repository yet. If you add tests, place them in a sibling project such as `tray-ui.Tests/` and use a `*.Tests` naming pattern (for example, `NoteTests`). Run tests with `dotnet test` once a test project is available. Prefer descriptive test method names like `MethodName_Scenario_ExpectedResult`.

## Commit & Pull Request Guidelines
No Git history is present in this checkout, so follow simple, imperative commit summaries (for example, `ui: adjust title bar layout`). Keep the first line under 72 characters. For pull requests, include a concise description, steps to validate, and screenshots or screen recordings for UI changes. Link related issues and call out any packaging or manifest changes.

## Security & Configuration Tips
Be deliberate with capabilities in `tray-ui/Package.appxmanifest` and avoid adding permissions you do not need. Changes to `tray-ui/app.manifest` and publish profiles in `tray-ui/Properties/PublishProfiles/` affect packaging and deployment, so note them clearly in PRs.
