$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$payload=Join-Path $PSScriptRoot 'payload'
$output=Join-Path $PSScriptRoot 'AI-PUBG-Setup.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$payload\AI-PUBG-Switcher.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32icon:$payload\AI-icon.ico" "/resource:$PSScriptRoot\comss-source.pac,comss-source.pac" "$PSScriptRoot\Switcher.cs"
if($LASTEXITCODE -ne 0){throw 'Application compilation failed'}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$output" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/win32icon:$payload\AI-icon.ico" "/resource:$payload\AI-PUBG-Switcher.exe,AI-PUBG-Switcher.exe" "/resource:$payload\AI-icon.ico,AI-icon.ico" "/resource:$payload\AI-icon.png,AI-icon.png" "/resource:$payload\Readme.txt,Readme.txt" "$PSScriptRoot\Setup.cs"
if($LASTEXITCODE -ne 0){throw 'Installer compilation failed'}
Write-Output $output
