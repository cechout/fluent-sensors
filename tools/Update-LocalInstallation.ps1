param(
    [string]$RestoreFrom
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$target = 'C:\Program Files\Fluent Sensors'
$payload = Join-Path $repo 'FluentSensors\bin\x64\Release\net10.0-windows10.0.19041.0\publish\win-x64'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$logFolder = Join-Path $repo 'logs'
New-Item -ItemType Directory -Force -Path $logFolder | Out-Null
$resultPath = Join-Path $logFolder "installation-$stamp.json"
$backup = Join-Path $repo "artifacts\installation-backups\$stamp"
$changed = $false
$result = @{ Status = 'Failed'; Backup = $backup; Target = $target; Error = $null }

function Copy-Contents([string]$From, [string]$To) {
    New-Item -ItemType Directory -Force -Path $To | Out-Null
    Get-ChildItem -LiteralPath $From -Force | Copy-Item -Destination $To -Recurse -Force
}

function Stop-InstalledApp {
    foreach ($app in Get-Process -Name FluentSensors -ErrorAction SilentlyContinue) {
        if ($app.Path -eq (Join-Path $target 'FluentSensors.exe')) {
            # Only this installed copy; other portable builds are left alone.
            Stop-Process -Id $app.Id
            $app.WaitForExit(10000) | Out-Null
            if (!$app.HasExited) { throw 'The installed application did not exit.' }
        }
    }
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (!([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this helper as administrator to update Program Files.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $target 'FluentSensors.exe'))) {
        throw 'The expected installed application was not found.'
    }
    if ($RestoreFrom) {
        $source = (Resolve-Path -LiteralPath $RestoreFrom).Path
        $backupRoot = [IO.Path]::GetFullPath((Join-Path $repo 'artifacts\installation-backups')) + '\'
        if (!$source.StartsWith($backupRoot, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Rollback must use a backup inside this contribution.'
        }
        $source = Join-Path $source 'app'
    } else {
        $source = $payload
    }
    if (!(Test-Path -LiteralPath (Join-Path $source 'FluentSensors.dll'))) {
        throw 'Build and publish the application before updating, or select a valid backup.'
    }

    # Take a full rollback copy before stopping or writing anything in Program Files.
    Copy-Contents $target (Join-Path $backup 'app')
    $settings = Join-Path $env:LOCALAPPDATA 'FluentSensors'
    if (Test-Path -LiteralPath $settings) {
        Copy-Contents $settings (Join-Path $backup 'settings')
    }
    Stop-InstalledApp
    $changed = $true
    Copy-Contents $source $target
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
        $relative = $file.FullName.Substring($source.TrimEnd('\').Length + 1)
        $destination = Join-Path $target $relative
        if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) {
            throw "Copied file failed verification: $relative"
        }
    }
    $started = Start-Process -FilePath (Join-Path $target 'FluentSensors.exe') -PassThru
    Start-Sleep -Seconds 10
    if ($started.HasExited) { throw "Application exited during startup: $($started.ExitCode)" }
    $result.Status = 'Updated'
    $result.ProcessId = $started.Id
    $result.AssemblySHA256 = (Get-FileHash -LiteralPath (Join-Path $target 'FluentSensors.dll')).Hash
} catch {
    $result.Error = $_.Exception.Message
    if ($changed) {
        try {
            Stop-InstalledApp
            Copy-Contents (Join-Path $backup 'app') $target
            Start-Process -FilePath (Join-Path $target 'FluentSensors.exe') | Out-Null
            $result.Status = 'RolledBack'
        } catch {
            $result.RollbackError = $_.Exception.Message
        }
    }
} finally {
    $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath
}
if ($result.Status -ne 'Updated') { exit 1 }
