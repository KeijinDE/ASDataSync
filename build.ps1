# Builds AzerothSync.exe with the C# compiler of .NET Framework 4 (part of every Windows).
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1 [-DeployTo "<sync folder>"]
# -DeployTo copies the exe and docs\HANDOVER.md into that folder.
param([string]$DeployTo)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$ico = Join-Path $here 'src\app.ico'
$game = 'C:\Games\AzerothSurvivors\AzerothSurvivors.exe'
# Use the game's icon if the game is installed locally (not part of the repository)
if (-not (Test-Path $ico) -and (Test-Path $game)) {
    Add-Type -AssemblyName System.Drawing
    $icon = [System.Drawing.Icon]::ExtractAssociatedIcon($game)
    $fs = [IO.File]::Create($ico); $icon.Save($fs); $fs.Close()
}
$out = Join-Path $here 'AzerothSync.exe'
$iconArg = if (Test-Path $ico) { "/win32icon:$ico" } else { $null }
& $csc /nologo /target:winexe /optimize+ /codepage:65001 "/out:$out" $iconArg `
    "/win32manifest:$(Join-Path $here 'src\app.manifest')" `
    /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    (Join-Path $here 'src\AssemblyInfo.cs') (Join-Path $here 'src\Engine.cs') (Join-Path $here 'src\Theme.cs') (Join-Path $here 'src\Look.cs') (Join-Path $here 'src\App.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host "Built: $out"
if ($DeployTo) {
    Copy-Item $out, (Join-Path $here 'docs\HANDOVER.md') $DeployTo -Force
    Write-Host "Copied to $DeployTo"
}
