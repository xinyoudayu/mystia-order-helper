<#
    update.ps1 - check and install a new MystiaOrderHelper build from GitHub Releases.

    Usage:
        .\update.ps1                      check; install if a newer version exists
        .\update.ps1 -Check               check only, never install
        .\update.ps1 -Force               reinstall even when the version matches
        .\update.ps1 -Repo owner/name     override repo (default: xinyoudayu/mystia-order-helper)
        .\update.ps1 -GamePath "D:\...\Touhou Mystia Izakaya"
        .\update.ps1 -LocalZip <path>     install from a local zip (offline / testing)
        .\update.ps1 -Token <PAT>         access token for a private repo

    Notes:
        - Out-of-game only: read installed version -> query releases/latest -> compare
          -> download zip -> archive old dll -> install new dll
        - The old dll is never deleted; it is moved to BepInEx\plugins\_backup\
        - Refuses to replace files while the game is running
        - THIS FILE MUST STAY PURE ASCII. PowerShell 5.1 reads .ps1 as ANSI on a zh-CN
          system, so non-ASCII text corrupts parsing (even line numbering).
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$Force,
    [string]$Repo = 'xinyoudayu/mystia-order-helper',
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya',
    [string]$ApiBase = 'https://api.github.com',
    [string]$LocalZip = '',
    [string]$Token = ''
)

$ErrorActionPreference = 'Stop'
$PluginFilePrefix = 'MystiaOrderHelper-v'
$ProcessName = 'Touhou Mystia Izakaya'
$RepoPlaceholder = 'OWNER/mystia-order-helper'

$work = $null

function Write-Step($text) { Write-Host "  $text" }

function Remove-WorkDir {
    if ($work -and $work -like "$env:TEMP\moh-update-*" -and (Test-Path -LiteralPath $work)) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Fail($text) {
    Remove-WorkDir
    Write-Host ""
    Write-Host "  [x] $text" -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  Mystia Order Helper updater" -ForegroundColor Cyan
Write-Host "  ------------------------------------"

# --- 1. locate the game folder ---
if (-not (Test-Path -LiteralPath $GamePath)) {
    Fail "Game folder not found: $GamePath`n      Run with -GamePath <your game folder>."
}
$plugins = Join-Path $GamePath 'BepInEx\plugins'
if (-not (Test-Path -LiteralPath $plugins)) {
    Fail "BepInEx\plugins not found under $GamePath`n      Install BepInEx 6.0.0-be.785 (IL2CPP) first."
}

# --- 2. read installed version ---
$installed = @(Get-ChildItem -LiteralPath $plugins -File -Filter "$PluginFilePrefix*.dll" |
    Sort-Object LastWriteTime -Descending)
$installedVersion = $null
if ($installed.Count -gt 0 -and $installed[0].BaseName -match 'v(\d+(?:\.\d+)+)$') {
    $installedVersion = [version]$Matches[1]
}
if ($installedVersion) {
    Write-Step "Installed : v$installedVersion  ($($installed[0].Name))"
} else {
    Write-Step "Installed : (not installed)"
}

# --- 3. obtain the zip (remote release or local file) ---
$download = $null
$assetName = $null
$latestVersion = $null
$latestTag = $null

# Everything is extracted and cleaned up inside this temp folder only.
$work = Join-Path $env:TEMP ("moh-update-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $work -Force | Out-Null

if ($LocalZip) {
    if (-not (Test-Path -LiteralPath $LocalZip)) { Fail "Local zip not found: $LocalZip" }
    $download = (Resolve-Path -LiteralPath $LocalZip).Path
    $assetName = Split-Path -Path $download -Leaf
    $latestTag = '(local zip)'
    Write-Step "Repo      : local file $assetName"
} else {
    if (-not $Repo) {
        # Optional fallback: derive owner/name from this clone. Never fatal - players
        # run this script without git installed, and the default above already works.
        $gitDir = Join-Path $PSScriptRoot '.git'
        if ((Test-Path -LiteralPath $gitDir) -and (Get-Command git -ErrorAction SilentlyContinue)) {
            try {
                $remote = (& git -C $PSScriptRoot remote get-url origin 2>$null)
                if ($remote -and $remote -match 'github\.com[:/]([^/]+)/([^/\s]+?)(?:\.git)?$') {
                    $Repo = "$($Matches[1])/$($Matches[2])"
                }
            } catch {
                $Repo = ''
            }
        }
    }
    if (-not $Repo) { $Repo = $RepoPlaceholder }
    if ($Repo -eq $RepoPlaceholder) {
        Fail "Repo not configured. Pass -Repo owner/name, or run this script inside the git clone`n      (after 'git remote add origin https://github.com/<you>/mystia-order-helper.git')."
    }
    Write-Step "Repo      : $Repo"

    $headers = @{
        'User-Agent' = 'MystiaOrderHelper-Updater'
        'Accept'     = 'application/vnd.github+json'
    }
    if ($Token) { $headers['Authorization'] = "token $Token" }

    $apiUrl = "$ApiBase/repos/$Repo/releases/latest"
    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -UseBasicParsing -TimeoutSec 30
    } catch {
        $code = $null
        if ($_.Exception.Response) { $code = [int]$_.Exception.Response.StatusCode }
        if ($code -eq 404) {
            Fail "No published release found for $Repo.`n      Publish one: upload the zip on the Releases page with tag vX.Y.Z."
        }
        Fail "Cannot reach GitHub ($($_.Exception.Message)).`n      Check your network, or use -LocalZip with a downloaded zip."
    }

    $latestTag = $release.tag_name
    if (-not $latestTag) { Fail "Release has no tag name." }
    if ($latestTag -match '(\d+(?:\.\d+)+)') { $latestVersion = [version]$Matches[1] }
    Write-Step "Latest    : $latestTag  (published $($release.published_at))"

    $assets = @($release.assets)
    if ($assets.Count -eq 0) {
        Fail "Release $latestTag has no attached files. Upload the zip to the release first."
    }
    $asset = $assets | Where-Object { $_.name -match '\.zip$' } | Select-Object -First 1
    if (-not $asset) {
        $asset = $assets | Where-Object { $_.name -match '\.dll$' } | Select-Object -First 1
    }
    if (-not $asset) {
        Fail "Release $latestTag has no .zip or .dll asset (found: $($assets.name -join ', '))."
    }
    $assetName = $asset.name
    Write-Step "Asset     : $assetName  ($([math]::Round($asset.size / 1KB, 1)) KB)"

    $download = Join-Path $work $asset.name
    Write-Step "Download  : ..."
    try {
        if ($Token -and $asset.url) {
            Invoke-WebRequest -Uri $asset.url -Headers $headers -OutFile $download -UseBasicParsing -TimeoutSec 120
        } else {
            Invoke-WebRequest -Uri $asset.browser_download_url -Headers @{ 'User-Agent' = $headers['User-Agent'] } -OutFile $download -UseBasicParsing -TimeoutSec 120
        }
    } catch {
        Fail "Download failed: $($_.Exception.Message)"
    }
    Write-Step "Downloaded: $([math]::Round((Get-Item -LiteralPath $download).Length / 1KB, 1)) KB"
}

# --- 4. extract the dll and resolve the new version ---
$newDll = $null
if ($download -match '\.zip$') {
    $extract = Join-Path $work 'extract'
    try {
        if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
        Expand-Archive -LiteralPath $download -DestinationPath $extract -Force
    } catch {
        Fail "Extract failed: $($_.Exception.Message)"
    }
    $candidate = Get-ChildItem -LiteralPath $extract -Recurse -File -Filter "$PluginFilePrefix*.dll" |
        Sort-Object Length -Descending | Select-Object -First 1
    if (-not $candidate) { Fail "No ${PluginFilePrefix}X.Y.Z.dll inside $assetName." }
    $newDll = $candidate.FullName
} else {
    $newDll = $download
}

$newName = Split-Path -Path $newDll -Leaf
if ($newName -notmatch "^$([regex]::Escape($PluginFilePrefix))(\d+(?:\.\d+)+)\.dll$") {
    Fail "Unexpected dll name '$newName' (expected ${PluginFilePrefix}X.Y.Z.dll)."
}
$newVersion = [version]$Matches[1]
if (-not $latestVersion) { $latestVersion = $newVersion }
if ($newVersion -ne $latestVersion) {
    Write-Step "Warn      : dll version $newVersion != release tag $latestTag"
}

# --- 5. compare ---
if ($installedVersion -and -not $Force -and $newVersion -le $installedVersion) {
    Remove-WorkDir
    Write-Host ""
    Write-Host "  Already up to date (v$installedVersion). Use -Force to reinstall." -ForegroundColor Green
    Write-Host ""
    exit 0
}
if (-not $installedVersion) {
    Write-Step "Action    : fresh install v$newVersion"
} elseif ($newVersion -gt $installedVersion) {
    Write-Step "Action    : upgrade v$installedVersion -> v$newVersion"
} else {
    Write-Step "Action    : reinstall v$newVersion (forced)"
}

if ($Check) {
    Remove-WorkDir
    Write-Host ""
    Write-Host "  Check only (-Check): nothing installed." -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

# --- 6. the game must be closed ---
$running = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue
if ($running) {
    Fail "Game is running (PID $($running[0].Id)). Close it, then run this script again."
}

# --- 7. archive the old dll, install the new one ---
$backup = Join-Path $plugins '_backup'
if (-not (Test-Path -LiteralPath $backup)) {
    New-Item -ItemType Directory -Path $backup -Force | Out-Null
}
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$newStem = [System.IO.Path]::GetFileNameWithoutExtension($newName)
foreach ($old in $installed) {
    if ($old.BaseName -eq $newStem) { continue }
    $target = Join-Path $backup ("$($old.BaseName).$stamp.dll")
    Move-Item -LiteralPath $old.FullName -Destination $target -Force
    Write-Step "Archived  : $($old.Name) -> _backup\$([System.IO.Path]::GetFileName($target))"
}

$dest = Join-Path $plugins $newName
Copy-Item -LiteralPath $newDll -Destination $dest -Force

# --- 8. verify ---
$srcHash = (Get-FileHash -LiteralPath $newDll -Algorithm MD5).Hash
$dstHash = (Get-FileHash -LiteralPath $dest -Algorithm MD5).Hash
Remove-WorkDir
if ($srcHash -ne $dstHash) {
    Fail "Copy verification failed (md5 mismatch). The old dll is in _backup\."
}

Write-Host ""
Write-Host "  Done: v$newVersion installed. md5 $dstHash" -ForegroundColor Green
Write-Host "  Start the game and press F8." -ForegroundColor Green
Write-Host ""
