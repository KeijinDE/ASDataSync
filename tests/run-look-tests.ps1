$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot
$out=Join-Path $PSScriptRoot 'AzerothSyncLookTests.exe'
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /main:AzerothSync.LookTests /codepage:65001 "/out:$out" `
    /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    "$repo\src\AssemblyInfo.cs" "$repo\src\Engine.cs" "$repo\src\Theme.cs" "$repo\src\Look.cs" "$repo\src\App.cs" "$repo\src\Setup.cs" "$repo\src\Wizard.cs" "$PSScriptRoot\LookTests.cs"
if($LASTEXITCODE -ne 0){throw 'UI test build failed'}
& $out
if($LASTEXITCODE -ne 0){throw 'UI tests failed'}
