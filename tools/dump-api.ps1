# 编译并运行 ApiDump（程序集结构查询）。只读，不碰游戏文件。
#   .\tools\dump-api.ps1 type GameData.Core.Collections.Ingredient
#   .\tools\dump-api.ps1 find Extra Topping 加料
#   .\tools\dump-api.ps1 enum CookerType
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Mode,
    [Parameter(Mandatory = $true, Position = 1, ValueFromRemainingArguments = $true)][string[]]$Keys,
    [string]$Assembly = 'Assembly-CSharp.dll',
    [string]$DotnetRoot = (Join-Path $env:USERPROFILE 'Documents\deepseek-harness\default-workspace\.dotnet'),
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Touhou Mystia Izakaya'
)

$ErrorActionPreference = 'Stop'

$root    = Split-Path $PSScriptRoot -Parent
$sdk     = Join-Path $DotnetRoot 'sdk\6.0.428'
$csc     = Join-Path $sdk 'Roslyn\bincore\csc.dll'
$dotnet  = Join-Path $DotnetRoot 'dotnet.exe'
$stage   = Join-Path $env:USERPROFILE 'Documents\deepseek-harness\default-workspace\.tmp\apidump'
$outDll  = Join-Path $stage 'ApiDump.dll'
$cecil   = Join-Path $GamePath 'BepInEx\core\Mono.Cecil.dll'
$target  = Join-Path $GamePath ('BepInEx\interop\' + $Assembly)

if (-not (Test-Path $csc))   { throw "compiler not found: $csc" }
if (-not (Test-Path $cecil)) { throw "Mono.Cecil not found: $cecil" }
if (-not (Test-Path $target)){ throw "assembly not found: $target" }

New-Item -ItemType Directory -Force -Path $stage | Out-Null

$pack = Get-ChildItem (Join-Path $DotnetRoot 'packs\Microsoft.NETCore.App.Ref') -Directory |
        Sort-Object Name -Descending | Select-Object -First 1
$refs = @(Get-ChildItem (Join-Path $pack.FullName 'ref\net6.0') -Filter *.dll | ForEach-Object { '-r:' + $_.FullName })
$refs += '-r:' + $cecil

& $dotnet $csc /nologo /nostdlib /target:exe /langversion:10 /nullable:disable `
    "/out:$outDll" @refs (Join-Path $root 'tools\ApiDump.cs')
if ($LASTEXITCODE -ne 0) { throw 'compile failed' }

Copy-Item $cecil $stage -Force
$runtimeConfig = Join-Path $stage 'ApiDump.runtimeconfig.json'
@'
{ "runtimeOptions": { "tfm": "net6.0", "framework": { "name": "Microsoft.NETCore.App", "version": "6.0.0" } } }
'@ | Set-Content -Path $runtimeConfig -Encoding utf8

& $dotnet $outDll $target $Mode @Keys
exit $LASTEXITCODE
