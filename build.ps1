$ErrorActionPreference = "Stop"
$projDir = "C:\Users\sweet\.gemini\antigravity\scratch\khammam-street-racer"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$refDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"

Write-Host "1. Compiling KhammamStreetRacer.exe..." -ForegroundColor Cyan
& $csc /nologo /target:winexe /optimize+ `
  /out:"$projDir\dist\KhammamStreetRacer.exe" `
  "$projDir\src\KhammamStreetRacer.cs"

if ($LASTEXITCODE -ne 0) { throw "Failed to compile KhammamStreetRacer.exe" }

Write-Host "2. Packaging payload.zip for single-file Setup.exe..." -ForegroundColor Cyan
$stageDir = "$projDir\src\stage_payload"
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path "$stageDir\assets" | Out-Null

Copy-Item "$projDir\dist\KhammamStreetRacer.exe" "$stageDir\" -Force
Copy-Item "$projDir\version.json" "$stageDir\" -Force
Copy-Item "$projDir\github-config.json" "$stageDir\" -Force
Copy-Item "$projDir\assets\*.jpg" "$stageDir\assets\" -Force

$zipPath = "$projDir\src\payload.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $zipPath)
Remove-Item $stageDir -Recurse -Force

Write-Host "3. Compiling standalone installer Khammam-Street-Racer-Setup.exe..." -ForegroundColor Cyan
& $csc /nologo /target:winexe /optimize+ `
  /r:"$refDir\System.IO.Compression.dll" `
  /r:"$refDir\System.IO.Compression.FileSystem.dll" `
  /resource:"$zipPath,payload.zip" `
  /out:"$projDir\dist\Khammam-Street-Racer-Setup.exe" `
  "$projDir\src\KhammamSetup.cs"

if ($LASTEXITCODE -ne 0) { throw "Failed to compile Khammam-Street-Racer-Setup.exe" }
Remove-Item $zipPath -Force

Write-Host "✅ Build complete!" -ForegroundColor Green
Get-Item "$projDir\dist\*.exe" | Select-Object Name, Length, LastWriteTime
