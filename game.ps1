param(
    [ValidateSet('start', 'stop', 'restart', 'status')]
    [string]$Action = 'status',

    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya',

    # Seconds to wait for a graceful window close before killing the process.
    [int]$StopTimeoutSec = 20,

    # Seconds to wait for BepInEx to write the log after launch.
    [int]$ReadyTimeoutSec = 90
)

$ErrorActionPreference = 'Stop'

$processName = 'Touhou Mystia Izakaya'
$exe         = Join-Path $GamePath "$processName.exe"
$log         = Join-Path $GamePath 'BepInEx\LogOutput.log'

function Get-Game {
    Get-Process -Name $processName -ErrorAction SilentlyContinue
}

function Show-Status {
    $procs = Get-Game
    if (-not $procs) {
        Write-Host 'game: not running'
        return
    }
    foreach ($p in $procs) {
        $up = (Get-Date) - $p.StartTime
        Write-Host ("game: running pid={0} up={1:hh\:mm\:ss}" -f $p.Id, $up)
    }
    Show-PluginLog
}

function Show-PluginLog {
    if (-not (Test-Path $log)) { return }
    $lines = Select-String -Path $log -Pattern 'OrderHelper' -Encoding UTF8 |
             Select-Object -Last 12
    if (-not $lines) {
        Write-Host 'log: no [OrderHelper] lines yet'
        return
    }
    Write-Host 'log: last [OrderHelper] lines'
    $lines | ForEach-Object { Write-Host ('  ' + $_.Line) }
}

function Start-Game {
    if (Get-Game) {
        Write-Host 'game: already running'
        return
    }
    if (-not (Test-Path $exe)) { throw "exe not found: $exe" }

    $started = Get-Date
    Start-Process -FilePath $exe -WorkingDirectory $GamePath
    Write-Host 'game: launched, waiting for process and log...'

    # The launcher may exit immediately while the real game process starts later,
    # so poll instead of checking once.
    $deadline = (Get-Date).AddSeconds($ReadyTimeoutSec)
    $seen = $false
    $goneSince = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 800
        $proc = Get-Game

        if ($proc) {
            $seen = $true
            $goneSince = $null
        }
        elseif ($seen -and -not $goneSince) {
            $goneSince = Get-Date
        }

        # The launcher may hand off to another process; only treat a long absence as a failure.
        if ($goneSince -and ((Get-Date) - $goneSince).TotalSeconds -ge 15) {
            throw 'game exited during startup - check the BepInEx console'
        }

        if ($proc -and (Test-Path $log) -and
            (Get-Item $log).LastWriteTime -gt $started -and
            (Select-String -Path $log -Pattern 'OrderHelper' -Quiet -ErrorAction SilentlyContinue)) {
            break
        }
    }

    if (-not $seen) { throw 'game process never appeared within the timeout' }
    if (-not (Get-Game)) { throw 'game is not running any more' }

    Write-Host 'game: up'
    Show-PluginLog
}

function Stop-Game {
    $procs = Get-Game
    if (-not $procs) {
        Write-Host 'game: not running'
        return
    }

    Write-Host 'game: requesting close...'
    foreach ($p in $procs) { $null = $p.CloseMainWindow() }

    $deadline = (Get-Date).AddSeconds($StopTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not (Get-Game)) {
            Write-Host 'game: closed gracefully'
            return
        }
        Start-Sleep -Milliseconds 500
    }

    Write-Host 'game: still open, killing process (unsaved progress may be lost)'
    foreach ($p in Get-Game) { try { $p.Kill() } catch { } }
    Start-Sleep -Seconds 3
    if (Get-Game) { throw 'failed to stop the game' }
    Write-Host 'game: stopped'
}

switch ($Action) {
    'start'   { Start-Game }
    'stop'    { Stop-Game }
    'restart' { Stop-Game; Start-Sleep -Seconds 2; Start-Game }
    'status'  { Show-Status }
}
