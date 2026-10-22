# Compiles src\Engine.cs with the tests and runs them. Exit code 0 = all passed.
# Usage: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = Split-Path $here
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path ([IO.Path]::GetTempPath()) 'AzerothSyncTests.exe'
& $csc /nologo /codepage:65001 "/out:$out" `
    /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:System.Core.dll `
    (Join-Path $repo 'src\Engine.cs') (Join-Path $repo 'src\Setup.cs') (Join-Path $here 'EngineTests.cs') (Join-Path $here 'SetupTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
& $out
exit $LASTEXITCODE
