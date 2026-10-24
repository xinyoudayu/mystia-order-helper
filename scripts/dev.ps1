param(
    [switch]$NoBuild,
    [switch]$Start,
    # 只有明确加 -Force 才允许关闭正在运行的游戏
    [switch]$Force
)

# 约定：绝不主动关闭用户正在运行的游戏。
# 游戏开着 → 直接拒绝部署，请用户自己退出后再跑。
$ErrorActionPreference = 'Stop'
# 本脚本在 scripts\ 下，项目根在上一级（build.ps1 在根目录）
$root = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path (Join-Path $root 'build.ps1'))) { $root = $PSScriptRoot }
$processName = 'Touhou Mystia Izakaya'

$running = Get-Process -Name $processName -ErrorAction SilentlyContinue
if ($running -and -not $Force) {
    Write-Host 'game is running. Close it yourself, then run this script again.'
    Write-Host '(dev.ps1 never stops the game unless you pass -Force)'
    exit 1
}

if ($running) {
    Write-Host 'game: stopping because -Force was passed'
    & (Join-Path $PSScriptRoot 'game.ps1') stop
}

if (-not $NoBuild) {
    & (Join-Path $root 'build.ps1') -Install
}

Write-Host ''
Write-Host 'deploy done. Start the game yourself when ready (start.bat), or pass -Start.'

if ($Start) {
    & (Join-Path $PSScriptRoot 'game.ps1') start
}
