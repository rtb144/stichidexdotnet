# Builds a self-contained win-x64 StichidexDotNet and an Inno Setup installer (side-by-side with Python Stitchidex).
param([string]$Version = "0.5.0", [string]$Iscc = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "artifacts\win-x64"
dotnet publish (Join-Path $root "src\Stitchidex.Desktop\Stitchidex.Desktop.csproj") -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $out
Copy-Item (Join-Path $out "stichidexdotnet.exe") (Join-Path $out "stitchidexdotnet.exe") -Force
Copy-Item (Join-Path $out "stichidexdotnet.exe") (Join-Path $out "Stitchidex.exe") -Force
Get-ChildItem "$env:USERPROFILE\.nuget\packages" -Recurse -Filter *.dll -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -match 'runtimes\\win-x64\\native' -and $_.Name -match 'onnxruntime|e_sqlite3|libSkiaSharp|av_libglesv2|libHarfBuzzSharp' } |
  ForEach-Object { Copy-Item $_.FullName $out -Force }
Copy-Item (Join-Path $root "docs\Stitchidex-Windows-User-Guide.html") (Join-Path $out "User-Guide.html") -Force
$models = Join-Path $out "models\bge-base-en-v1.5"
New-Item -ItemType Directory -Force $models | Out-Null
$cache = Join-Path $env:LOCALAPPDATA "Stitchidex\models\bge-base-en-v1.5"
foreach ($f in @(@("model_quantized.onnx","onnx/model_quantized.onnx"), @("vocab.txt","vocab.txt"))) {
  $dest = Join-Path $models $f[0]
  if (Test-Path $dest) { continue }
  if (Test-Path (Join-Path $cache $f[0])) { Copy-Item (Join-Path $cache $f[0]) $dest }
  else { Invoke-WebRequest "https://huggingface.co/Xenova/bge-base-en-v1.5/resolve/main/$($f[1])" -OutFile $dest }
}
if (-not $Iscc) {
  foreach ($c in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) { if (Test-Path $c) { $Iscc = $c; break } }
}
if (-not $Iscc) { winget install -e --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements --silent
  foreach ($c in @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")) { if (Test-Path $c) { $Iscc = $c; break } } }
$iss = Join-Path $root "artifacts\stichidexdotnet.iss"
@"
[Setup]
AppId={{7E1C3D5A-5B1F-4C8E-9A1D-57A1C0DE0500}
AppName=Stitchidex (.NET)
AppVersion=$Version
AppPublisher=Stitchidex
DefaultDirName={autopf}\StichidexDotNet
DefaultGroupName=Stitchidex (.NET)
OutputDir=$(Join-Path $root "artifacts")
OutputBaseFilename=StichidexDotNet-Setup-$Version-win64
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=$(Join-Path $root "LICENSE")
SetupIconFile=$(Join-Path $root "src\Stitchidex.Desktop\Assets\stitchidex.ico")
UninstallDisplayIcon={app}\stichidexdotnet.exe
[Files]
Source: "$out\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
[Icons]
Name: "{group}\Stitchidex (.NET)"; Filename: "{app}\stichidexdotnet.exe"
Name: "{group}\Stitchidex User Guide"; Filename: "{app}\User-Guide.html"
Name: "{autodesktop}\Stitchidex (.NET)"; Filename: "{app}\stichidexdotnet.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; Flags: unchecked
[Run]
Filename: "{app}\stichidexdotnet.exe"; Description: "Launch Stitchidex (.NET)"; Flags: nowait postinstall skipifsilent
"@ | Set-Content -Encoding UTF8 $iss
if ($Iscc -and (Test-Path $Iscc)) { & $Iscc $iss } else { Write-Warning "Inno Setup not found at $Iscc; script written to $iss" }
