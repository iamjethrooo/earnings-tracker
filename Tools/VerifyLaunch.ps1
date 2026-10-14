[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$DataDirectory,
    [int]$TimeoutSeconds = 45
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$scratch = Join-Path $repository 'artifacts'
$directory = [IO.Path]::GetFullPath($DataDirectory)
if (-not $directory.StartsWith($scratch + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Launch tests require an artifacts-only data directory.' }
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw 'Executable missing.' }
New-Item -ItemType Directory -Force -Path $directory | Out-Null
if ((Get-Item -LiteralPath $directory).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Test directory cannot redirect to another location.' }
if (-not ('EarningsTrackerLaunchWindow' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class EarningsTrackerLaunchWindow {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
}
'@
}
$previous = [Environment]::GetEnvironmentVariable('NERVE_TRACKER_DATA_DIRECTORY', 'Process')
$process = $null
try {
    [Environment]::SetEnvironmentVariable('NERVE_TRACKER_DATA_DIRECTORY', $directory, 'Process')
    $process = Start-Process -FilePath ([IO.Path]::GetFullPath($Executable)) -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $process.Refresh()
        if ($process.HasExited) { throw "Application exited before opening its main window ($($process.ExitCode))." }
        $bounds = New-Object EarningsTrackerLaunchWindow+Rect
        if ($process.MainWindowHandle -ne [IntPtr]::Zero -and [EarningsTrackerLaunchWindow]::GetWindowRect($process.MainWindowHandle, [ref]$bounds) -and
            ($bounds.Right - $bounds.Left) -ge 1000 -and ($bounds.Bottom - $bounds.Top) -ge 600) { $ready = $true; break }
        Start-Sleep -Milliseconds 200
    }
    if (-not $ready) { throw "Main window did not become ready within $TimeoutSeconds seconds." }
    if (-not $process.CloseMainWindow()) { throw 'Main window refused normal close.' }
    if (-not $process.WaitForExit(10000)) { throw 'Isolated launch did not shut down normally.' }
    if ($process.ExitCode -ne 0) { throw "Application exit code: $($process.ExitCode)" }
    if (-not (Test-Path -LiteralPath (Join-Path $directory 'EarningsTracker.db'))) { throw 'Isolated database was not created.' }
    [ordered]@{ Executable = [IO.Path]::GetFullPath($Executable); DataDirectory = $directory; MainWindowReady = $ready;
        ExitCode = $process.ExitCode; DatabaseCreated = $true } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $directory 'launch-verification.json') -Encoding utf8
    Write-Output "Isolated launch passed: $directory"
} finally {
    [Environment]::SetEnvironmentVariable('NERVE_TRACKER_DATA_DIRECTORY', $previous, 'Process')
    if ($process) {
        $process.Refresh()
        if (-not $process.HasExited) { $null = $process.CloseMainWindow() }
        # A failed test may leave its own dialog open; preserve it for inspection, never kill a user's app.
        $process.Dispose()
    }
}
