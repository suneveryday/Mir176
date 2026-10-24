param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'build'))
$ErrorActionPreference='Stop'
New-Item -ItemType Directory $OutputDirectory -Force | Out-Null
$output=(Resolve-Path $OutputDirectory).Path
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if(!(Test-Path $csc)){throw 'Official .NET Framework compiler not found'}
$inputs=@('Program.cs','LauncherCore.cs','LauncherWindow.cs')|ForEach-Object{Join-Path $PSScriptRoot $_}
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /debug- /codepage:65001 "/out:$output\Mir176Launcher.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $inputs
if($LASTEXITCODE -ne 0){throw 'Launcher compile failed'}
Copy-Item (Join-Path $PSScriptRoot 'README.txt') $output -Force
@{name='Mir176Launcher.exe';sha256=(Get-FileHash (Join-Path $output 'Mir176Launcher.exe') -Algorithm SHA256).Hash.ToLowerInvariant();version=(Get-Item (Join-Path $output 'Mir176Launcher.exe')).VersionInfo.FileVersion}|ConvertTo-Json
