# Repository Guidelines

## Project Structure & Module Organization
The WinUI 3 desktop app lives under `tray-ui/`. Core entry points are `tray-ui/App.xaml`, `tray-ui/App.xaml.cs`, and `tray-ui/MainWindow.xaml`. App services live in `tray-ui/Services/`, and view models live in `tray-ui/ViewModels/`. App assets and icons are stored in `tray-ui/Assets/`. Packaging and app configuration are in `tray-ui/Package.appxmanifest` and `tray-ui/app.manifest`, while launch settings live in `tray-ui/Properties/`. Shared/core logic lives under `tray-ui.Core/` with models in `tray-ui.Core/Models/` and services in `tray-ui.Core/Services/`. Unit tests live in `tray-ui.Tests/`. Build outputs are under `tray-ui/bin/` and `tray-ui/obj/` and should not be edited manually.

## Build, Test, and Development Commands
- `dotnet build tray-ui/tray-ui.csproj -r win-x64` builds the WinUI app.
- `dotnet test tray-ui.Tests/tray-ui.Tests.csproj` runs the unit tests.

## Coding Style & Naming Conventions
Use 4-space indentation in both C# and XAML. Follow standard .NET naming: PascalCase for classes, methods, and properties; camelCase for local variables and private fields. Page files should use the `*Page.xaml` suffix, and `x:Name` should be added only for elements referenced in code-behind. Nullable reference types are enabled in `tray-ui/tray-ui.csproj`, so address warnings rather than suppressing them.

## Testing Guidelines
Tests live in `tray-ui.Tests/` and use a `*.Tests` naming pattern (for example, `AppSettingsTests`). Prefer descriptive test method names like `MethodName_Scenario_ExpectedResult`.

## Commit & Pull Request Guidelines
No Git history is present in this checkout, so follow simple, imperative commit summaries (for example, `ui: adjust title bar layout`). Keep the first line under 72 characters. For pull requests, include a concise description, steps to validate, and screenshots or screen recordings for UI changes. Link related issues and call out any packaging or manifest changes.

## Security & Configuration Tips
Be deliberate with capabilities in `tray-ui/Package.appxmanifest` and avoid adding permissions you do not need. Changes to `tray-ui/app.manifest` and publish profiles in `tray-ui/Properties/PublishProfiles/` affect packaging and deployment, so note them clearly in PRs.
