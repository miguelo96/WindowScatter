# monitor-lag.ps1
# Reproduces the WindowScatter "laggy return animation after idle" bug on demand:
# opens the overlay (injected Win+W), idles for a configurable time, then closes it
# (Esc) while capturing per-frame animation timings, GC counts, CPU and memory.
#
# Frame timing + GC stats are written by the app itself (must be launched via this
# script, which sets WINDOWSCATTER_FRAMELOG=1) to:
#   $env:TEMP\WindowScatter\frames-{scatter|return}-*.csv
#
# Usage:
#   .\monitor-lag.ps1                                  # 3 cycles x 10s idle, Debug build
#   .\monitor-lag.ps1 -Cycles 5 -IdleSeconds 30
#   .\monitor-lag.ps1 -SpawnWindows 9                  # repro the multi-window scenario
#   .\monitor-lag.ps1 -ExePath "...\WindowScatter.exe"
#
# Compare "frames-return-*.csv" summaries (avg_ms / max_ms / frames_over_33ms and
# the gc_during_anim line) before/after long idle periods to verify the fix.

[CmdletBinding()]
param(
    [int]$Cycles = 3,
    [int]$IdleSeconds = 10,
    [string]$ExePath = "",
    [string]$OutCsv = "",
    [int]$SpawnWindows = 0   # open N throwaway console windows first (multi-window repro)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutCsv)) {
    $OutCsv = Join-Path $PSScriptRoot "monitor-lag-results.csv"
}

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class KeySynth {
    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    public const uint KEYUP = 0x0002;
    public const byte VK_LWIN = 0x5B;
    public const byte VK_W    = 0x57;
    public const byte VK_ESC  = 0x1B;
}
"@

function Send-Hotkey {
    # Win+W (matches the app's default hotkey; the low-level hook receives injected events)
    [KeySynth]::keybd_event([KeySynth]::VK_LWIN, 0, 0, [UIntPtr]::Zero)
    [KeySynth]::keybd_event([KeySynth]::VK_W, 0, 0, [UIntPtr]::Zero)
    [KeySynth]::keybd_event([KeySynth]::VK_W, 0, [KeySynth]::KEYUP, [UIntPtr]::Zero)
    [KeySynth]::keybd_event([KeySynth]::VK_LWIN, 0, [KeySynth]::KEYUP, [UIntPtr]::Zero)
}

function Send-Escape {
    [KeySynth]::keybd_event([KeySynth]::VK_ESC, 0, 0, [UIntPtr]::Zero)
    [KeySynth]::keybd_event([KeySynth]::VK_ESC, 0, [KeySynth]::KEYUP, [UIntPtr]::Zero)
}

function Get-Sample([System.Diagnostics.Process]$proc, [string]$phase, [int]$cycle) {
    $proc.Refresh()
    [pscustomobject]@{
        Cycle        = $cycle
        Phase        = $phase
        Timestamp    = (Get-Date).ToString("HH:mm:ss.fff")
        CpuSeconds   = [math]::Round($proc.TotalProcessorTime.TotalSeconds, 2)
        WorkingSetMB = [math]::Round($proc.WorkingSet64 / 1MB, 1)
        PrivateMB    = [math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
    }
}

# --- Launch ---------------------------------------------------------------

$existing = Get-Process -Name "WindowScatter" -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping running WindowScatter instance(s) so frame logging can be enabled..."
    $existing | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $PSScriptRoot "..\bin\Debug\net8.0-windows10.0.26100.0\WindowScatter.exe"
}
$ExePath = [System.IO.Path]::GetFullPath($ExePath)
if (-not (Test-Path $ExePath)) { throw "Executable not found: $ExePath - build the project first." }

Write-Host "Launching: $ExePath (WINDOWSCATTER_FRAMELOG=1)"
$psi = [System.Diagnostics.ProcessStartInfo]::new($ExePath)
$psi.UseShellExecute = $false
$psi.Environment["WINDOWSCATTER_FRAMELOG"] = "1"
[System.Diagnostics.Process]::Start($psi) | Out-Null
Start-Sleep -Seconds 2

$proc = Get-Process -Name "WindowScatter" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $proc) { throw "Failed to start WindowScatter." }

$logDir = Join-Path $env:TEMP "WindowScatter"
$knownLogs = @{}
if (Test-Path $logDir) {
    Get-ChildItem $logDir -Filter "frames-*.csv" | ForEach-Object { $knownLogs[$_.FullName] = $true }
}

Write-Host "Attached to PID $($proc.Id). Running $Cycles cycle(s), ${IdleSeconds}s idle each."
Write-Host "Frame logs: $logDir\frames-*.csv"

# --- Optional: spawn throwaway windows for the multi-window repro -------------

$spawned = @()
if ($SpawnWindows -gt 0) {
    Write-Host "Spawning $SpawnWindows throwaway console windows..."
    for ($k = 1; $k -le $SpawnWindows; $k++) {
        $spawned += Start-Process cmd.exe -ArgumentList "/k title ws-spawn-$k" -PassThru
        Start-Sleep -Milliseconds 150
    }
    Start-Sleep -Seconds 1
}

# --- Test loop --------------------------------------------------------------

$results = New-Object System.Collections.Generic.List[object]

try {
for ($i = 1; $i -le $Cycles; $i++) {
    Write-Host "Cycle $i/$Cycles : opening overlay (Win+W)..."
    $results.Add((Get-Sample $proc "before-open" $i))

    Send-Hotkey
    Start-Sleep -Milliseconds 800          # scatter animation settles

    $results.Add((Get-Sample $proc "overlay-open" $i))

    Write-Host "  idling ${IdleSeconds}s..."
    Start-Sleep -Seconds $IdleSeconds
    $results.Add((Get-Sample $proc "after-idle" $i))

    Write-Host "  closing (Esc) -> return animation..."
    Send-Escape
    Start-Sleep -Milliseconds 1200         # return animation + cleanup

    $results.Add((Get-Sample $proc "after-close" $i))
}
}
finally {
    foreach ($p in $spawned) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}

# --- Report -------------------------------------------------------------------

$results | Format-Table -AutoSize
$results | Export-Csv -Path $OutCsv -NoTypeInformation
Write-Host "Wrote $OutCsv"

$newLogs = @()
if (Test-Path $logDir) {
    $newLogs = @(Get-ChildItem $logDir -Filter "frames-*.csv" |
        Where-Object { -not $knownLogs.ContainsKey($_.FullName) } |
        Sort-Object LastWriteTime)
}

if ($newLogs.Count -gt 0) {
    Write-Host "`nFrame log summaries for this run:"
    foreach ($f in $newLogs) {
        $lines = Get-Content $f.FullName | Select-String '^#'
        Write-Host "  $($f.Name)"
        foreach ($l in $lines) { Write-Host "    $($l.Line -replace '^# ', '')" }
    }
} else {
    Write-Host "`nNo new frame logs produced. Did the overlay open? (needs >=1 normal window on screen)"
}
