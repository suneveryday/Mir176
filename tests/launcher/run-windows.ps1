param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(!$OutputDirectory){throw 'Provide a new isolated test output directory'}
if(Test-Path $OutputDirectory){throw 'Keep previous evidence; use a fresh directory'}
New-Item -ItemType Directory $OutputDirectory|Out-Null
$output=(Resolve-Path $OutputDirectory).Path
$source=Join-Path $PSScriptRoot '..\..\launcher'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /debug- /optimize+ /codepage:65001 "/out:$output\LauncherTests.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $source 'LauncherCore.cs') (Join-Path $source 'LauncherWindow.cs') (Join-Path $PSScriptRoot 'LauncherTests.cs')
if($LASTEXITCODE -ne 0){throw 'Launcher test compile failed'}
& $csc /nologo /target:exe /debug- "/out:$output\ClientStub.exe" (Join-Path $PSScriptRoot 'ClientStub.cs')
if($LASTEXITCODE -ne 0){throw 'Stub compile failed'}
& (Join-Path $output 'LauncherTests.exe') (Join-Path $output 'isolated-install') (Join-Path $output 'ClientStub.exe')
if($LASTEXITCODE -ne 0){throw 'Launcher tests failed'}
