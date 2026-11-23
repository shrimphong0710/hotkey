$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $projectDir 'QuickText.cs'
$outputPath = Join-Path $projectDir 'QuickText.exe'
$iconPath = Join-Path $projectDir 'assets\app.ico'
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Force }
& $compiler /nologo /target:winexe /platform:anycpu "/win32icon:$iconPath" "/out:$outputPath" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw "C# compiler failed with exit code $LASTEXITCODE" }
Write-Host "Built: $outputPath"
