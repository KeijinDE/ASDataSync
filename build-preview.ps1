$ErrorActionPreference='Stop'
$csc='C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output=Join-Path $PSScriptRoot 'AzerothSync.DesignPreview.exe'
& $csc /nologo /target:winexe /main:AzerothSync.DesignProgram /codepage:65001 "/out:$output" `
    /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    "$PSScriptRoot\src\AssemblyInfo.cs" "$PSScriptRoot\src\Engine.cs" "$PSScriptRoot\src\Theme.cs" "$PSScriptRoot\src\Look.cs" "$PSScriptRoot\src\App.cs" "$PSScriptRoot\tests\DesignPreview.cs"
if($LASTEXITCODE -ne 0){throw 'Preview build failed'}
Write-Host "Design preview: $output"
