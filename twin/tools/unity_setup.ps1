# Creates / updates the Unity HDRP project and runs the benchmark build. Repeatable; each step logs to unity\logs\.
# Usage: powershell -ExecutionPolicy Bypass -File tools\unity_setup.ps1 [-From create|packages|scene|player|bench]
param([string]$From = "create", [string]$Editor = "C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe")
$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent
$Proj = Join-Path $Root "unity\QandeelTwin"
$Logs = Join-Path $Root "unity\logs"
New-Item -ItemType Directory -Force $Logs | Out-Null
$steps = "create", "packages", "scene", "player", "bench"
$start = [array]::IndexOf($steps, $From)

function Unity([string]$name, [string[]]$extra) {
    $log = Join-Path $Logs "$name.log"
    $uargs = @("-batchmode", "-quit", "-logFile", "`"$log`"") + $extra
    Write-Host "== $name (log: $log)"
    $p = Start-Process -FilePath $Editor -ArgumentList $uargs -Wait -PassThru -NoNewWindow
    $errs = Select-String -Path $log -Pattern "error CS\d+|\[Qandeel\]|Exception" -ErrorAction SilentlyContinue | Select-Object -First 15
    $errs | ForEach-Object { Write-Host "   $($_.Line)" }
    if ($p.ExitCode -ne 0) { throw "$name failed (exit $($p.ExitCode)); see $log" }
}

function CopySrc([string]$from, [string]$to) {
    New-Item -ItemType Directory -Force (Join-Path $Proj $to) | Out-Null
    Copy-Item (Join-Path $Root "unity_src\$from\*") (Join-Path $Proj $to) -Recurse -Force
}

if ($start -le 0 -and -not (Test-Path (Join-Path $Proj "ProjectSettings"))) {
    Unity "1_create" @("-createProject", "`"$Proj`"")
}
if ($start -le 1) {
    CopySrc "Runtime" "Assets\Qandeel\Runtime"
    CopySrc "Editor" "Assets\Qandeel\Editor"
    Unity "2_packages" @("-projectPath", "`"$Proj`"", "-executeMethod", "Qandeel.EditorTools.Bootstrap.AddPackages")
}
if ($start -le 2) {
    CopySrc "EditorHDRP" "Assets\Qandeel\Editor"
    CopySrc "Runtime" "Assets\Qandeel\Runtime"
    CopySrc "UI" "Assets\UI"
    Unity "3_scene" @("-projectPath", "`"$Proj`"", "-executeMethod", "Qandeel.EditorTools.SiteBuilder.Build")
}
if ($start -le 3) {
    Unity "4_player" @("-projectPath", "`"$Proj`"", "-executeMethod", "Qandeel.EditorTools.SiteBuilder.BuildPlayer")
}
if ($start -le 4) {
    $exe = Join-Path $Proj "Builds\Qandeel\Qandeel.exe"
    Write-Host "== 5_bench (1920x1080 window, 8 s warm-up + 20 s measure)"
    Start-Process -FilePath $exe -ArgumentList "-benchmark", "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0" -Wait
    Get-Content (Join-Path $Proj "Builds\Qandeel\benchmark.json")
}
