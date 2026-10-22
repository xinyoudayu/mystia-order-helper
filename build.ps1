param(
    [switch]$Install,
    [string]$DotnetRoot = (Join-Path $env:USERPROFILE 'Documents\deepseek-harness\default-workspace\.dotnet'),
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya'
)

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$name    = 'MystiaOrderHelper'
$version = '1.4.9'
$sdk     = Join-Path $DotnetRoot 'sdk\6.0.428'
$csc     = Join-Path $sdk 'Roslyn\bincore\csc.dll'
$dotnet  = Join-Path $DotnetRoot 'dotnet.exe'
$outDir  = Join-Path $root 'dist'
$outDll  = Join-Path $outDir "$name-v$version.dll"

# D:\temp is not writable on this machine, and csc cannot write to E: here.
# Compile into a staging dir on C:, then copy the result into dist\.
$stage    = Join-Path $env:USERPROFILE 'Documents\deepseek-harness\default-workspace\.tmp\orderhelper'
$stageOut = Join-Path $stage "$name-v$version.dll"
$env:TEMP = $stage
$env:TMP  = $stage
New-Item -ItemType Directory -Force -Path $stage, $outDir | Out-Null

if (-not (Test-Path $csc))    { throw "compiler not found: $csc" }
if (-not (Test-Path $dotnet)) { throw "dotnet not found: $dotnet" }

# .NET 6 reference pack
$pack = Get-ChildItem (Join-Path $DotnetRoot 'packs\Microsoft.NETCore.App.Ref') -Directory |
        Sort-Object Name -Descending | Select-Object -First 1
if (-not $pack) { throw 'reference pack not found' }
$refs = @(Get-ChildItem (Join-Path $pack.FullName 'ref\net6.0') -Filter *.dll |
          ForEach-Object { '-r:' + $_.FullName })

# BepInEx core (dobby.dll is native, must be excluded)
$refs += @(Get-ChildItem (Join-Path $GamePath 'BepInEx\core') -Filter *.dll |
           Where-Object { $_.Name -ne 'dobby.dll' } |
           ForEach-Object { '-r:' + $_.FullName })

# Game interop assemblies
$interop = Join-Path $GamePath 'BepInEx\interop'
$modules = @(
    'Il2Cppmscorlib.dll', 'Il2CppSystem.dll', 'Il2CppSystem.Core.dll', 'Assembly-CSharp.dll',
    'Assembly-CSharp-firstpass.dll',
    'UnityEngine.CoreModule.dll', 'UnityEngine.IMGUIModule.dll',
    'UnityEngine.InputLegacyModule.dll', 'Unity.InputSystem.dll',
    'UnityEngine.TextRenderingModule.dll', 'UnityEngine.UIModule.dll',
    'UnityEngine.UI.dll',
    'Unity.TextMeshPro.dll'
)
foreach ($m in $modules) {
    $p = Join-Path $interop $m
    if (-not (Test-Path $p)) { throw "missing interop assembly: $p" }
    $refs += '-r:' + $p
}

# Third-party runtime assemblies the game types derive from
$refs += @(Get-ChildItem $interop -Filter 'DEYU*.dll' | ForEach-Object { '-r:' + $_.FullName })

$sources = Get-ChildItem (Join-Path $root 'src\OrderHelper') -Filter *.cs -Recurse |
           ForEach-Object { $_.FullName }
if (-not $sources) { throw 'no .cs files under src\OrderHelper' }

& $dotnet exec $csc -nologo -target:library -nostdlib+ -langversion:10 -codepage:65001 `
    ('-out:' + $stageOut) @refs @sources

if ($LASTEXITCODE -ne 0) { throw "compile failed, exit code $LASTEXITCODE" }
if (-not (Test-Path $stageOut)) { throw "no output: $stageOut" }

Copy-Item $stageOut $outDll -Force
if (-not (Test-Path $outDll)) { throw "copy to dist failed: $outDll" }

$bytes = [IO.File]::ReadAllBytes($outDll)
if ($bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { throw 'output is not a PE file' }

$size = [math]::Round((Get-Item $outDll).Length / 1KB, 1)
Write-Host "build ok: $outDll ($size KB)"

if ($Install) {
    $plugins = Join-Path $GamePath 'BepInEx\plugins'
    if (-not (Test-Path $plugins)) { throw "plugins dir not found: $plugins" }
    # Remove older builds first: two copies of the same plugin GUID would both load.
    Get-ChildItem $plugins -Filter "$name-v*.dll" -File | Remove-Item -Force
    Copy-Item $outDll $plugins -Force
    Write-Host "installed to: $plugins\$name-v$version.dll"
}









