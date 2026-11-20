$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourcePath = Join-Path $projectDir 'QuickText.cs'
$outputPath = Join-Path $projectDir 'QuickText.exe'
if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Force }
Add-Type -Path $sourcePath -ReferencedAssemblies 'System.dll','System.Drawing.dll','System.Windows.Forms.dll' -OutputAssembly $outputPath -OutputType WindowsApplication
Write-Host "Built: $outputPath"
