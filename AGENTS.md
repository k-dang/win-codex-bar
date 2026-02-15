# Repository Guidelines

## Project Structure & Module Organization

- The WinUI 3 desktop app lives under `WinCodexBar.UI/`.
- Core entry points are `WinCodexBar.UI/App.xaml`, `WinCodexBar.UI/App.xaml.cs`, and `WinCodexBar.UI/MainWindow.xaml`.
- Additional UI pages/windows include `WinCodexBar.UI/SettingsPage.xaml` and `WinCodexBar.UI/SettingsWindow.cs`.
- App services live in `WinCodexBar.UI/Services/`, view models in `WinCodexBar.UI/ViewModels/`, and views in `WinCodexBar.UI/Views/`.
- App assets and icons are in `WinCodexBar.UI/Assets/`.
- Packaging and app configuration are in `WinCodexBar.UI/Package.appxmanifest` and `WinCodexBar.UI/app.manifest`, while launch and publish settings live in `WinCodexBar.UI/Properties/`.

Shared/core logic lives under `WinCodexBar.Core/` with models in `WinCodexBar.Core/Models/` and services in `WinCodexBar.Core/Services/`. Unit tests live in `WinCodexBar.Tests/`. Build outputs are under each project's `bin/` and `obj/` directories and should not be edited manually.

## Build, Test, and Development Commands

- `dotnet restore WinCodexBar.UI/WinCodexBar.UI.csproj` restores dependencies.
- `dotnet build WinCodexBar.UI/WinCodexBar.UI.csproj -r win-x64` builds the WinUI app.
- `dotnet test WinCodexBar.Tests/WinCodexBar.Tests.csproj` runs the unit tests.

## Coding Style & Naming Conventions

Follow standard .NET naming: PascalCase for classes, methods, and properties; camelCase for local variables and private fields. Page files should use the `*Page.xaml` suffix, and `x:Name` should be added only for elements referenced in code-behind. Nullable reference types are enabled in `WinCodexBar.UI/WinCodexBar.UI.csproj`, so address warnings rather than suppressing them.

## Testing Guidelines

Tests live in `WinCodexBar.Tests/` and should follow a `*Tests` file naming pattern (for example, `AppSettingsTests`). Prefer descriptive test method names like `MethodName_Scenario_ExpectedResult`.
