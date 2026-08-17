#Requires -Version 5.1
<#
.SYNOPSIS
Drives a Win Codex Bar instance started by this verification skill.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File .cursor/skills/verify-win-codex-bar/scripts/control-wincodexbar.ps1 launch
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet("launch", "doctor", "show", "click", "snapshot", "screenshot", "wait", "cleanup")]
    [string]$Command,

    [string]$Id,
    [string]$Name,
    [string]$Path,
    [ValidateSet("main", "settings", "tray")]
    [string]$Window = "main",
    [int]$TimeoutSeconds = 45
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SkillDir = Split-Path -Parent $ScriptDir
$RunDir = Join-Path $SkillDir ".run"
$StatePath = Join-Path $RunDir "state.json"
$DefaultArtifacts = Join-Path $SkillDir "artifacts"

$WindowTitles = @{
    main     = "Win Codex Bar"
    settings = "Settings"
    tray     = "Tray Menu"
}

function Get-RepoRoot {
    $dir = $SkillDir
    while ($dir) {
        if (Test-Path (Join-Path $dir "BuildAndRun.ps1")) {
            return $dir
        }
        $parent = Split-Path -Parent $dir
        if ($parent -eq $dir) {
            break
        }
        $dir = $parent
    }
    throw "Could not find repo root (BuildAndRun.ps1) above $SkillDir."
}

function Ensure-UiAutomation {
    Add-Type -AssemblyName UIAutomationClient | Out-Null
    Add-Type -AssemblyName UIAutomationTypes | Out-Null
}

function Get-State {
    if (-not (Test-Path $StatePath)) {
        throw "No verification run state at $StatePath. Run launch first."
    }
    return Get-Content -Raw -Path $StatePath | ConvertFrom-Json
}

function Save-State {
    param($State)
    New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
    $State | ConvertTo-Json -Depth 6 | Set-Content -Path $StatePath -Encoding UTF8
}

function Get-WinCodexBarProcesses {
    @(Get-Process -Name "WinCodexBar" -ErrorAction SilentlyContinue)
}

function Get-SettingsPath {
    return Join-Path $env:LOCALAPPDATA "WinCodexBar\settings.json"
}

function Get-LogDirectory {
    return Join-Path $env:LOCALAPPDATA "WinCodexBar\logs"
}

function Assert-TrackedProcess {
    param($State)
    $proc = Get-Process -Id $State.pid -ErrorAction SilentlyContinue
    if (-not $proc) {
        throw "Tracked WinCodexBar pid $($State.pid) is not running."
    }
    if ($proc.ProcessName -ne "WinCodexBar") {
        throw "Pid $($State.pid) is $($proc.ProcessName), not WinCodexBar."
    }
    return $proc
}

function Get-WindowElement {
    param(
        [int]$ProcessId,
        [string]$Title
    )
    Ensure-UiAutomation
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition (
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId
    )
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition (
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Title
    )
    $andCondition = New-Object System.Windows.Automation.AndCondition ($pidCondition, $nameCondition)
    $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $andCondition)
    if ($null -eq $window) {
        $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCondition)
    }
    return $window
}

function Wait-WindowElement {
    param(
        [int]$ProcessId,
        [string]$Title,
        [int]$TimeoutSeconds
    )
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $window = Get-WindowElement -ProcessId $ProcessId -Title $Title
        if ($null -ne $window) {
            return $window
        }
        Start-Sleep -Milliseconds 250
    } while ([datetime]::UtcNow -lt $deadline)
    throw "Timed out after ${TimeoutSeconds}s waiting for window '$Title' on pid $ProcessId."
}

function Show-NativeWindow {
    param([int]$Hwnd)
    if ($Hwnd -le 0) {
        return
    }
    if (-not ("WinCodexBarNative" -as [type])) {
        Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WinCodexBarNative {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
    }
    [void][WinCodexBarNative]::ShowWindow([IntPtr]$Hwnd, 9) # SW_RESTORE
    [void][WinCodexBarNative]::SetForegroundWindow([IntPtr]$Hwnd)
}

function Find-ByNameAndType {
    param(
        $Root,
        [string]$Name,
        $ControlType
    )
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition (
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name
    )
    $typeCondition = New-Object System.Windows.Automation.PropertyCondition (
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        $ControlType
    )
    $andCondition = New-Object System.Windows.Automation.AndCondition ($nameCondition, $typeCondition)
    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $andCondition)
}

function Find-Descendant {
    param(
        $Root,
        [string]$AutomationId,
        [string]$Name
    )
    if ($AutomationId) {
        $condition = New-Object System.Windows.Automation.PropertyCondition (
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            $AutomationId
        )
        $found = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $found) {
            return $found
        }
    }
    if ($Name) {
        foreach ($controlType in @(
                [System.Windows.Automation.ControlType]::ListItem,
                [System.Windows.Automation.ControlType]::Button,
                [System.Windows.Automation.ControlType]::TabItem,
                [System.Windows.Automation.ControlType]::CheckBox,
                [System.Windows.Automation.ControlType]::ComboBox
            )) {
            $found = Find-ByNameAndType -Root $Root -Name $Name -ControlType $controlType
            if ($null -ne $found) {
                return $found
            }
        }
        $condition = New-Object System.Windows.Automation.PropertyCondition (
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name
        )
        $found = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $found) {
            return $found
        }
    }
    return $null
}

function Invoke-Element {
    param($Element)
    $pattern = $null
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
        $pattern.Select()
        return
    }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
        $pattern.Invoke()
        return
    }
    if ($Element.TryGetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern, [ref]$pattern)) {
        $pattern.Toggle()
        return
    }
    $name = $Element.Current.Name
    $typeName = $Element.Current.ControlType.ProgrammaticName
    throw "Element '$name' ($typeName) has no Select/Invoke/Toggle pattern."
}

function Write-Tree {
    param(
        $Element,
        [int]$Depth,
        [System.Text.StringBuilder]$Builder,
        [int]$MaxDepth = 10,
        [ref]$Remaining
    )
    if ($Depth -gt $MaxDepth -or $Remaining.Value -le 0) {
        return
    }
    $Remaining.Value--
    $indent = "  " * $Depth
    $typeName = $Element.Current.ControlType.ProgrammaticName -replace "^ControlType\.", ""
    $name = $Element.Current.Name
    $autoId = $Element.Current.AutomationId
    $line = "$indent$typeName"
    if ($name) { $line += " name=`"$name`"" }
    if ($autoId) { $line += " id=$autoId" }
    [void]$Builder.AppendLine($line)
    $children = $Element.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition
    )
    foreach ($child in $children) {
        Write-Tree -Element $child -Depth ($Depth + 1) -Builder $Builder -MaxDepth $MaxDepth -Remaining $Remaining
    }
}

function Backup-SettingsIfPresent {
    $settingsPath = Get-SettingsPath
    if (-not (Test-Path $settingsPath)) {
        return $null
    }
    New-Item -ItemType Directory -Force -Path $RunDir | Out-Null
    $backup = Join-Path $RunDir "settings.json.bak"
    Copy-Item -Path $settingsPath -Destination $backup -Force
    return $backup
}

function Restore-SettingsBackup {
    param($BackupPath)
    if (-not $BackupPath -or -not (Test-Path $BackupPath)) {
        return
    }
    $settingsPath = Get-SettingsPath
    $dir = Split-Path -Parent $settingsPath
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item -Path $BackupPath -Destination $settingsPath -Force
}

function Invoke-Launch {
    $existing = @(Get-WinCodexBarProcesses)
    if (@($existing).Count -gt 0) {
        $pids = ($existing | ForEach-Object { $_.Id }) -join ", "
        throw "WinCodexBar is already running (pid $pids). Refusing to drive a shared instance. Exit the user's app first."
    }

    $repoRoot = Get-RepoRoot
    $buildScript = Join-Path $repoRoot "BuildAndRun.ps1"
    Write-Host "Launching via $buildScript -Detach"
    $output = & $buildScript -Detach 2>&1 | ForEach-Object { $_.ToString() }
    $output | ForEach-Object { Write-Host $_ }

    $pidValue = $null
    $jsonLine = $output | Where-Object { $_ -match '^\s*\{' } | Select-Object -Last 1
    if ($jsonLine) {
        try {
            $parsed = $jsonLine | ConvertFrom-Json
            foreach ($key in @("pid", "Pid", "processId", "ProcessId", "process_id")) {
                if ($parsed.PSObject.Properties.Name -contains $key -and $parsed.$key) {
                    $pidValue = [int]$parsed.$key
                    break
                }
            }
        } catch {
            # Fall through to process scan.
        }
    }

    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($null -eq $pidValue -and [datetime]::UtcNow -lt $deadline) {
        $procs = @(Get-WinCodexBarProcesses)
        if (@($procs).Count -eq 1) {
            $pidValue = [int]$procs[0].Id
            break
        }
        Start-Sleep -Milliseconds 400
    }

    if ($null -eq $pidValue) {
        throw "Launch finished but WinCodexBar pid was not found. Last output:`n$($output -join "`n")"
    }

    $window = Wait-WindowElement -ProcessId $pidValue -Title $WindowTitles.main -TimeoutSeconds $TimeoutSeconds
    Show-NativeWindow -Hwnd ([int]$window.Current.NativeWindowHandle)

    $settingsButton = $null
    $waitUntil = [datetime]::UtcNow.AddSeconds(15)
    do {
        $settingsButton = Find-Descendant -Root $window -AutomationId "SettingsButton" -Name "Settings"
        if ($null -ne $settingsButton) { break }
        Start-Sleep -Milliseconds 250
        $window = Get-WindowElement -ProcessId $pidValue -Title $WindowTitles.main
    } while ([datetime]::UtcNow -lt $waitUntil)

    if ($null -eq $settingsButton) {
        throw "Main window opened but Settings button was not found. The instance is not worth driving."
    }

    $backup = Backup-SettingsIfPresent
    $state = [pscustomobject]@{
        pid            = $pidValue
        startedUtc     = [datetime]::UtcNow.ToString("o")
        repoRoot       = $repoRoot
        settingsBackup = $backup
        settingsPath   = Get-SettingsPath
        logDirectory   = Get-LogDirectory
    }
    Save-State -State $state
    Write-Host "Launched WinCodexBar pid=$pidValue"
    $state | ConvertTo-Json
}

function Invoke-Doctor {
    $state = Get-State
    $proc = Assert-TrackedProcess -State $state
    $others = @(Get-WinCodexBarProcesses | Where-Object { $_.Id -ne $state.pid })
    if (@($others).Count -gt 0) {
        $pids = ($others | ForEach-Object { $_.Id }) -join ", "
        throw "Extra WinCodexBar process(es) pid $pids. Refuse to drive a shared instance."
    }

    $window = Get-WindowElement -ProcessId $state.pid -Title $WindowTitles.main
    if ($null -eq $window) {
        throw "Tracked pid $($state.pid) has no main window."
    }

    $settingsButton = Find-Descendant -Root $window -AutomationId "SettingsButton" -Name "Settings"
    if ($null -eq $settingsButton) {
        throw "Main window is missing AutomationId SettingsButton."
    }

    $codexItem = Find-Descendant -Root $window -AutomationId $null -Name "Codex"
    Write-Host "pid=$($state.pid) name=$($proc.ProcessName) path=$($proc.Path)"
    Write-Host "window=$($window.Current.Name) settingsButton=ok codexItem=$(if ($codexItem) { 'ok' } else { 'missing' })"
    Write-Host "settings=$($state.settingsPath) logs=$($state.logDirectory)"
    Write-Host "healthy=true"
}

function Invoke-Show {
    $state = Get-State
    Assert-TrackedProcess -State $state | Out-Null
    $title = $WindowTitles[$Window]
    $window = Wait-WindowElement -ProcessId $state.pid -Title $title -TimeoutSeconds $TimeoutSeconds
    Show-NativeWindow -Hwnd ([int]$window.Current.NativeWindowHandle)
    Write-Host "Shown window '$title'"
}

function Get-TargetWindow {
    $state = Get-State
    Assert-TrackedProcess -State $state | Out-Null
    $title = $WindowTitles[$Window]
    return Wait-WindowElement -ProcessId $state.pid -Title $title -TimeoutSeconds 8
}

function Invoke-Click {
    if (-not $Id -and -not $Name) {
        throw "click requires -Id or -Name."
    }
    $window = Get-TargetWindow
    Show-NativeWindow -Hwnd ([int]$window.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 150
    $element = Find-Descendant -Root $window -AutomationId $Id -Name $Name
    if ($null -eq $element) {
        throw "No element with id='$Id' name='$Name' in window '$Window'."
    }
    Invoke-Element -Element $element
    $label = if ($Id) { $Id } else { $Name }
    Write-Host "Clicked $label"
}

function Invoke-Wait {
    if (-not $Id -and -not $Name) {
        throw "wait requires -Id or -Name."
    }
    $state = Get-State
    Assert-TrackedProcess -State $state | Out-Null
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $window = Get-WindowElement -ProcessId $state.pid -Title $WindowTitles[$Window]
        if ($null -ne $window) {
            $element = Find-Descendant -Root $window -AutomationId $Id -Name $Name
            if ($null -ne $element) {
                Write-Host "Found element"
                return
            }
        }
        Start-Sleep -Milliseconds 200
    } while ([datetime]::UtcNow -lt $deadline)
    throw "Timed out waiting for id='$Id' name='$Name' in window '$Window'."
}

function Invoke-Snapshot {
    if (-not $Path) {
        throw "snapshot requires -Path."
    }
    $window = Get-TargetWindow
    $builder = New-Object System.Text.StringBuilder
    $remaining = 400
    Write-Tree -Element $window -Depth 0 -Builder $builder -Remaining ([ref]$remaining)
    $outDir = Split-Path -Parent $Path
    if ($outDir) {
        New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    }
    $builder.ToString() | Set-Content -Path $Path -Encoding UTF8
    Write-Host "Wrote UIA snapshot $Path"
}

function Invoke-Screenshot {
    if (-not $Path) {
        throw "screenshot requires -Path."
    }
    $window = Get-TargetWindow
    Show-NativeWindow -Hwnd ([int]$window.Current.NativeWindowHandle)
    Start-Sleep -Milliseconds 200
    if (-not ("WinCodexBarNative" -as [type])) {
        Show-NativeWindow -Hwnd ([int]$window.Current.NativeWindowHandle)
    }
    Add-Type -AssemblyName System.Drawing | Out-Null
    $hwnd = [IntPtr][int]$window.Current.NativeWindowHandle
    $rect = New-Object WinCodexBarNative+RECT
    [void][WinCodexBarNative]::GetWindowRect($hwnd, [ref]$rect)
    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $hdc = $graphics.GetHdc()
        try {
            [void][WinCodexBarNative]::PrintWindow($hwnd, $hdc, 2)
        } finally {
            $graphics.ReleaseHdc($hdc)
        }
        $outDir = Split-Path -Parent $Path
        if ($outDir) {
            New-Item -ItemType Directory -Force -Path $outDir | Out-Null
        }
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
    Write-Host "Wrote screenshot $Path"
}

function Invoke-Cleanup {
    if (-not (Test-Path $StatePath)) {
        Write-Host "No run state; nothing to clean up."
        return
    }
    $state = Get-State
    $proc = Get-Process -Id $state.pid -ErrorAction SilentlyContinue
    if ($proc -and $proc.ProcessName -eq "WinCodexBar") {
        Stop-Process -Id $state.pid -Force
        try {
            $proc.WaitForExit(8000) | Out-Null
        } catch {
            # Process object may already be disposed.
        }
        Write-Host "Stopped pid $($state.pid)"
    } else {
        Write-Host "Tracked pid $($state.pid) already gone."
    }

    Restore-SettingsBackup -BackupPath $state.settingsBackup
    if ($state.settingsBackup) {
        Write-Host "Restored settings from $($state.settingsBackup)"
    }

    Remove-Item -Force $StatePath -ErrorAction SilentlyContinue
    Write-Host "Cleanup complete. Proof artifacts under $DefaultArtifacts were kept."
}

if (-not $Command) {
    throw "Usage: control-wincodexbar.ps1 <launch|doctor|show|click|snapshot|screenshot|wait|cleanup>"
}

switch ($Command) {
    "launch"     { Invoke-Launch }
    "doctor"     { Invoke-Doctor }
    "show"       { Invoke-Show }
    "click"      { Invoke-Click }
    "snapshot"   { Invoke-Snapshot }
    "screenshot" { Invoke-Screenshot }
    "wait"       { Invoke-Wait }
    "cleanup"    { Invoke-Cleanup }
}
